using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

/// <summary>Filtros aceitos por `GET /api/companies`.</summary>
public sealed record ListCompaniesQuery(
    string? Search = null,
    string? Status = null,
    int? BillingDay = null,
    bool? WithoutBillingDay = null,
    string? Source = null,
    bool? SeenInLastImport = null,
    string? AsaasLink = null);

public sealed record CreateCompanyRequest(
    string TaxId,
    string DisplayName,
    int? BillingDay,
    string? AsaasSandboxCustomerId,
    string? AsaasProductionCustomerId,
    string OperatorId);

public sealed record UpdateCompanyRequest(
    string DisplayName,
    int? BillingDay,
    string? AsaasSandboxCustomerId,
    string? AsaasProductionCustomerId,
    string OperatorId);

/// <summary>Corpo das ações que só precisam saber quem é o responsável.</summary>
public sealed record CompanyOperatorRequest(string OperatorId);

public sealed record CompanyRegistryAddressResponse(
    string Street,
    string Number,
    string Complement,
    string Neighborhood,
    string City,
    string State,
    string PostalCode)
{
    public static CompanyRegistryAddressResponse? FromDomain(CompanyRegistryAddress? registryAddress)
    {
        return registryAddress is null
            ? null
            : new CompanyRegistryAddressResponse(
                registryAddress.Street,
                registryAddress.Number,
                registryAddress.Complement,
                registryAddress.Neighborhood,
                registryAddress.City,
                registryAddress.State,
                registryAddress.PostalCode);
    }
}

/// <summary>
/// Empresa do catálogo reunida com a agenda atual. `BillingDay` é nulo enquanto
/// nenhum dia foi configurado.
/// </summary>
public sealed record CompanyResponse(
    string TaxId,
    string FormattedTaxId,
    string DisplayName,
    string? EvoName,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    CompanyRegistryAddressResponse? RegistryAddress,
    string RegistryLookupStatus,
    DateTimeOffset? RegistryLastCheckedAt,
    bool IsActive,
    string Source,
    int MemberCount,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    bool SeenInLastImport,
    bool RequiresReviewAfterReappearing,
    int? BillingDay,
    bool HasActiveSchedule,
    string? AsaasSandboxCustomerId,
    string? AsaasProductionCustomerId,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public static CompanyResponse FromDomain(
        Company company,
        CompanyBillingSchedule? companyBillingSchedule,
        Guid? latestImportId)
    {
        return new CompanyResponse(
            company.TaxId,
            CompanyTaxId.Format(company.TaxId),
            company.DisplayName,
            company.EvoName,
            company.LegalName,
            company.TradeName,
            company.RegistrationStatus,
            CompanyRegistryAddressResponse.FromDomain(company.RegistryAddress),
            company.RegistryLookupStatus.ToString(),
            company.RegistryLastCheckedAt,
            company.IsActive,
            company.Source.ToString(),
            company.LastImportedMemberCount,
            company.FirstSeenAt,
            company.LastSeenAt,
            company.WasSeenInImport(latestImportId),
            company.RequiresReviewAfterReappearing,
            companyBillingSchedule?.BillingDay,
            companyBillingSchedule?.IsActive ?? false,
            company.AsaasSandboxCustomerId,
            company.AsaasProductionCustomerId,
            company.UpdatedAt,
            company.UpdatedBy);
    }
}

/// <summary>Pessoa vista para a empresa na sincronização mais recente.</summary>
public sealed record CompanyMemberResponse(
    string MemberName,
    string? ContractName,
    int SourceRowNumber)
{
    public static CompanyMemberResponse FromDomain(CompanyCatalogImportMember importedMember)
    {
        return new CompanyMemberResponse(
            importedMember.MemberName,
            importedMember.ContractName,
            importedMember.SourceRowNumber);
    }
}

/// <summary>Uma prévia de faturamento já criada para a empresa.</summary>
public sealed record CompanyBillingHistoryEntryResponse(
    Guid BillingDraftId,
    Guid BillingPeriodId,
    string Status,
    int Version,
    int ItemCount,
    decimal TotalAmount,
    string? AsaasPaymentId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt)
{
    public static CompanyBillingHistoryEntryResponse FromDomain(BillingDraft billingDraft)
    {
        return new CompanyBillingHistoryEntryResponse(
            billingDraft.Id,
            billingDraft.BillingPeriodId,
            billingDraft.Status.ToString(),
            billingDraft.Version,
            billingDraft.Items.Count,
            billingDraft.TotalAmount,
            billingDraft.AsaasPaymentId,
            billingDraft.ApprovedAt,
            billingDraft.CreatedAt);
    }
}
