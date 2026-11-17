using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Gera prévias a partir da base local de colaboradores e do valor combinado
/// com cada empresa. Não lê planilha. Consulta o EVO só para saber quando cada
/// colaborador entrou no convênio, e calcular o proporcional de entrada.
/// </summary>
/// <remarks>
/// O proporcional nunca reduz uma cobrança sem evidência: sem dia de fechamento,
/// com o EVO fora do ar ou sem contrato corporativo no histórico, o colaborador
/// é cobrado pelo valor cheio e a prévia devolve um aviso para quem aprova.
/// </remarks>
public sealed class CorporateBillingDraftService(
    IBillingPeriodRepository billingPeriodRepository,
    ICompanyRepository companyRepository,
    ICorporateMemberRepository corporateMemberRepository,
    IBillingDraftRepository billingDraftRepository,
    IAuditLogRepository auditLogRepository,
    ICompanyBillingScheduleRepository companyBillingScheduleRepository,
    CorporateMemberEntryDateService corporateMemberEntryDateService)
{
    public async Task<GenerateCorporateBillingDraftsResponse> GenerateAsync(
        BillingPeriodReference billingPeriodReference,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");
        if (billingPeriod.Status == BillingPeriodStatus.ChargesCreated)
        {
            throw new ConflictException("Não é possível criar prévias em uma competência encerrada.");
        }

        var existingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(
            billingPeriod.Id,
            cancellationToken);
        var existingDraftsByCompany = existingDrafts
            .GroupBy(billingDraft => billingDraft.CompanyTaxId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var companies = await companyRepository.ListAsync(cancellationToken);
        var companiesByTaxId = companies.ToDictionary(company => company.TaxId, StringComparer.Ordinal);
        var members = await corporateMemberRepository.ListAsync(cancellationToken);

        var schedules = await companyBillingScheduleRepository.ListAsync(cancellationToken);
        var closingDaysByTaxId = schedules
            .GroupBy(schedule => schedule.ExternalCompanyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ClosingDay, StringComparer.Ordinal);

        var created = new List<GeneratedBillingDraftResponse>();
        var skipped = new List<SkippedCompanyResponse>();
        var unknownContracts = new SortedSet<string>(StringComparer.Ordinal);
        var membersWithoutCompany = new List<MemberWithoutCompanyResponse>();
        var membersOutsidePeriod = new List<MemberOutsidePeriodResponse>();
        var prorationWarnings = new List<ProrationWarningResponse>();
        var generatedAt = DateTimeOffset.UtcNow;

        var companiesToGenerate = new List<(Company Company, CorporateMember[] Members)>();
        foreach (var membersOfCompany in GroupBillableMembers(
            members,
            companiesByTaxId,
            unknownContracts,
            membersWithoutCompany))
        {
            var company = companiesByTaxId[membersOfCompany.Key];
            var companyMembers = membersOfCompany.ToArray();
            var refusal = DescribeRefusal(company, existingDraftsByCompany);
            if (refusal is not null)
            {
                skipped.Add(new SkippedCompanyResponse(
                    company.TaxId,
                    company.DisplayName,
                    companyMembers.Length,
                    refusal));
                continue;
            }

            companiesToGenerate.Add((company, companyMembers));
        }

        // Uma única passada pelo EVO para todas as empresas, em lotes, em vez de
        // uma consulta por empresa.
        var entryDates = await corporateMemberEntryDateService.ResolveAsync(
            companiesToGenerate
                .Where(companyToGenerate => closingDaysByTaxId.ContainsKey(companyToGenerate.Company.TaxId))
                .SelectMany(companyToGenerate => companyToGenerate.Members)
                .Select(member => member.EvoMemberId)
                .ToArray(),
            cancellationToken);

        foreach (var (company, companyMembers) in companiesToGenerate)
        {
            var amountPerMember = company.AmountPerMember!.Value;
            DateOnly? closingDate = closingDaysByTaxId.TryGetValue(company.TaxId, out var closingDay)
                ? new DateOnly(billingPeriodReference.Year, billingPeriodReference.Month, closingDay)
                : null;
            if (closingDate is null)
            {
                prorationWarnings.Add(new ProrationWarningResponse(
                    company.TaxId,
                    company.DisplayName,
                    null,
                    null,
                    "Empresa sem dia de fechamento: o proporcional de entrada não foi calculado e todos foram cobrados pelo valor cheio."));
            }

            var items = new List<BillingDraftItem>();
            var proratedMembers = new List<ProratedMemberResponse>();
            foreach (var member in companyMembers)
            {
                if (closingDate is null)
                {
                    items.Add(CreateFullItem(member, amountPerMember));
                    continue;
                }

                if (!entryDates.EntryDates.TryGetValue(member.EvoMemberId, out var entryDate))
                {
                    var failure = entryDates.Failures.GetValueOrDefault(
                        member.EvoMemberId,
                        "Data de entrada não encontrada no Evo.");
                    prorationWarnings.Add(new ProrationWarningResponse(
                        company.TaxId,
                        company.DisplayName,
                        member.EvoMemberId,
                        member.MemberName,
                        $"{failure} Cobrado pelo valor cheio."));
                    items.Add(CreateFullItem(member, amountPerMember));
                    continue;
                }

                var proration = EntryProration.Calculate(amountPerMember, entryDate, closingDate.Value);
                switch (proration.Kind)
                {
                    case EntryProrationKind.OutsidePeriod:
                        membersOutsidePeriod.Add(new MemberOutsidePeriodResponse(
                            member.EvoMemberId,
                            member.MemberName,
                            company.DisplayName,
                            entryDate));
                        break;
                    case EntryProrationKind.Prorated:
                        items.Add(new BillingDraftItem(
                            $"{member.MemberName} — proporcional {proration.BilledDays}/{EntryProration.BaseDays} dias "
                            + $"(entrada em {entryDate:dd/MM/yyyy})",
                            1,
                            proration.Amount,
                            member.EvoMemberId.ToString()));
                        proratedMembers.Add(new ProratedMemberResponse(
                            member.EvoMemberId,
                            member.MemberName,
                            entryDate,
                            proration.BilledDays,
                            proration.Amount));
                        break;
                    default:
                        items.Add(CreateFullItem(member, amountPerMember));
                        break;
                }
            }

            if (items.Count == 0)
            {
                skipped.Add(new SkippedCompanyResponse(
                    company.TaxId,
                    company.DisplayName,
                    companyMembers.Length,
                    "Todos os colaboradores entraram depois do fechamento desta competência."));
                continue;
            }

            var version = ResolveNextVersion(company.TaxId, existingDraftsByCompany);
            var billingDraft = new BillingDraft(
                billingPeriod.Id,
                company.TaxId,
                company.DisplayName,
                company.TaxId,
                null,
                items,
                version,
                generatedAt);

            await billingDraftRepository.AddAsync(billingDraft, cancellationToken);
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "billing-draft.generated-from-catalog",
                    operatorId,
                    generatedAt,
                    billingPeriod.Id,
                    billingDraft.Id,
                    DescribeGeneration(company, items.Count, amountPerMember, closingDate, proratedMembers)),
                cancellationToken);

            created.Add(new GeneratedBillingDraftResponse(
                billingDraft.Id,
                company.TaxId,
                company.DisplayName,
                items.Count,
                amountPerMember,
                billingDraft.TotalAmount,
                closingDate,
                proratedMembers));
        }

        if (created.Count > 0)
        {
            billingPeriod.MarkAwaitingReview(generatedAt);
            await billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);
        }

        return new GenerateCorporateBillingDraftsResponse(
            created,
            skipped,
            unknownContracts,
            membersWithoutCompany,
            membersOutsidePeriod,
            prorationWarnings);
    }

    private static BillingDraftItem CreateFullItem(CorporateMember member, decimal amountPerMember)
    {
        return new BillingDraftItem(member.MemberName, 1, amountPerMember, member.EvoMemberId.ToString());
    }

    private static string DescribeGeneration(
        Company company,
        int itemCount,
        decimal amountPerMember,
        DateOnly? closingDate,
        IReadOnlyCollection<ProratedMemberResponse> proratedMembers)
    {
        var description = $"{itemCount} colaborador(es) x {amountPerMember:F2} para {company.DisplayName}";
        if (closingDate is null)
        {
            return description + ", sem dia de fechamento: proporcional não calculado.";
        }

        description += $", fechamento em {closingDate:dd/MM/yyyy}";
        if (proratedMembers.Count == 0)
        {
            return description + ".";
        }

        var prorated = string.Join(
            "; ",
            proratedMembers.Select(member =>
                $"{member.EvoMemberId} {member.BilledDays}/{EntryProration.BaseDays} dias desde {member.EntryDate:dd/MM/yyyy} = {member.Amount:F2}"));
        return $"{description}. Proporcional de entrada: {prorated}.";
    }

    private static IEnumerable<IGrouping<string, CorporateMember>> GroupBillableMembers(
        IReadOnlyCollection<CorporateMember> members,
        IReadOnlyDictionary<string, Company> companiesByTaxId,
        SortedSet<string> unknownContracts,
        List<MemberWithoutCompanyResponse> membersWithoutCompany)
    {
        var billableMembers = new List<CorporateMember>();
        foreach (var member in members.Where(member => member.IsActive))
        {
            var contractNames = member.Contracts
                .Select(contract => contract.ContractName)
                .ToArray();
            if (!contractNames.Any(BillableCorporateContracts.Includes))
            {
                AddUnknownCorporateContracts(contractNames, unknownContracts);
                continue;
            }

            if (!companiesByTaxId.ContainsKey(member.CompanyTaxId))
            {
                membersWithoutCompany.Add(
                    new MemberWithoutCompanyResponse(member.EvoMemberId, member.MemberName));
                continue;
            }

            billableMembers.Add(member);
        }

        return billableMembers.GroupBy(member => member.CompanyTaxId, StringComparer.Ordinal);
    }

    private static void AddUnknownCorporateContracts(
        IEnumerable<string?> contractNames,
        SortedSet<string> unknownContracts)
    {
        foreach (var contractName in contractNames)
        {
            if (!string.IsNullOrWhiteSpace(contractName)
                && contractName.Contains("CORPORATIVO", StringComparison.OrdinalIgnoreCase))
            {
                unknownContracts.Add(contractName.Trim());
            }
        }
    }

    private static string? DescribeRefusal(
        Company company,
        IReadOnlyDictionary<string, BillingDraft[]> existingDraftsByCompany)
    {
        if (existingDraftsByCompany.TryGetValue(company.TaxId, out var existingDrafts)
            && existingDrafts.Any(billingDraft =>
                billingDraft.Status is not BillingDraftStatus.Cancelled
                    and not BillingDraftStatus.Superseded))
        {
            return "Já existe uma prévia desta empresa nesta competência.";
        }

        if (!company.IsActive)
        {
            return "Empresa inativa no catálogo.";
        }

        if (company.AmountPerMember is not > 0m)
        {
            return "Empresa sem valor por colaborador cadastrado.";
        }

        return null;
    }

    private static int ResolveNextVersion(
        string companyTaxId,
        IReadOnlyDictionary<string, BillingDraft[]> existingDraftsByCompany)
    {
        return existingDraftsByCompany.TryGetValue(companyTaxId, out var existingDrafts)
            ? existingDrafts.Max(billingDraft => billingDraft.Version) + 1
            : 1;
    }
}
