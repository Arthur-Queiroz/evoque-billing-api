using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Gera prévias a partir da base local de colaboradores e do valor combinado
/// com cada empresa. Não lê planilha nem consulta o EVO.
/// </summary>
public sealed class CorporateBillingDraftService(
    IBillingPeriodRepository billingPeriodRepository,
    ICompanyRepository companyRepository,
    ICorporateMemberRepository corporateMemberRepository,
    IBillingDraftRepository billingDraftRepository,
    IAuditLogRepository auditLogRepository)
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

        var created = new List<GeneratedBillingDraftResponse>();
        var skipped = new List<SkippedCompanyResponse>();
        var unknownContracts = new SortedSet<string>(StringComparer.Ordinal);
        var membersWithoutCompany = new List<MemberWithoutCompanyResponse>();
        var generatedAt = DateTimeOffset.UtcNow;

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

            var amountPerMember = company.AmountPerMember!.Value;
            var feePerMember = company.FeePerMember;
            var version = ResolveNextVersion(company.TaxId, existingDraftsByCompany);
            var billingDraft = new BillingDraft(
                billingPeriod.Id,
                company.TaxId,
                company.DisplayName,
                company.TaxId,
                null,
                BuildItems(companyMembers, amountPerMember, feePerMember),
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
                    feePerMember is > 0m
                        ? $"{companyMembers.Length} colaborador(es) x {amountPerMember:F2} mais taxa de {feePerMember.Value:F2} por colaborador para {company.DisplayName}."
                        : $"{companyMembers.Length} colaborador(es) x {amountPerMember:F2} para {company.DisplayName}."),
                cancellationToken);

            created.Add(new GeneratedBillingDraftResponse(
                billingDraft.Id,
                company.TaxId,
                company.DisplayName,
                companyMembers.Length,
                amountPerMember,
                feePerMember,
                billingDraft.TotalAmount));
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
            membersWithoutCompany);
    }

    /// <summary>
    /// Uma linha por colaborador e, quando a empresa paga taxa administrativa,
    /// uma única linha de taxa no fim.
    ///
    /// A taxa é um item próprio, e não um acréscimo ao valor unitário de cada
    /// colaborador, porque o boleto e a nota levam só o total: se o valor for
    /// embutido, a distinção entre mensalidade e taxa desaparece do sistema e
    /// a conferência contra o controle operacional para de bater.
    /// </summary>
    private static IReadOnlyCollection<BillingDraftItem> BuildItems(
        IReadOnlyCollection<CorporateMember> companyMembers,
        decimal amountPerMember,
        decimal? feePerMember)
    {
        var items = companyMembers
            .Select(member => new BillingDraftItem(
                member.MemberName,
                1,
                amountPerMember,
                member.EvoMemberId.ToString()))
            .ToList();

        if (feePerMember is > 0m)
        {
            items.Add(new BillingDraftItem(
                "Taxa administrativa",
                companyMembers.Count,
                feePerMember.Value,
                null));
        }

        return items;
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
