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
        var companiesAlreadyDrafted = existingDrafts
            .Select(billingDraft => billingDraft.CompanyTaxId)
            .ToHashSet(StringComparer.Ordinal);

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
            var refusal = DescribeRefusal(company, companiesAlreadyDrafted);
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
            var billingDraft = new BillingDraft(
                billingPeriod.Id,
                company.TaxId,
                company.DisplayName,
                company.TaxId,
                null,
                companyMembers
                    .Select(member => new BillingDraftItem(
                        member.MemberName,
                        1,
                        amountPerMember,
                        member.EvoMemberId.ToString()))
                    .ToArray(),
                generatedAt);

            await billingDraftRepository.AddAsync(billingDraft, cancellationToken);
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "billing-draft.generated-from-catalog",
                    operatorId,
                    generatedAt,
                    billingPeriod.Id,
                    billingDraft.Id,
                    $"{companyMembers.Length} colaborador(es) x {amountPerMember:F2} para {company.DisplayName}."),
                cancellationToken);

            created.Add(new GeneratedBillingDraftResponse(
                billingDraft.Id,
                company.TaxId,
                company.DisplayName,
                companyMembers.Length,
                amountPerMember,
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
        IReadOnlySet<string> companiesAlreadyDrafted)
    {
        if (companiesAlreadyDrafted.Contains(company.TaxId))
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
}
