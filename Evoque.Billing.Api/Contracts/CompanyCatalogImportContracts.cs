using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Contracts;

/// <summary>
/// Prévia da sincronização do catálogo. Não contém valores financeiros: esta
/// importação descobre empresas e pessoas, não calcula cobrança.
/// </summary>
public sealed record CompanyCatalogImportPreviewResponse(
    string FileName,
    int AnalyzedRowCount,
    int DiscoveredCompanyCount,
    int DiscoveredMemberCount,
    int DuplicateMemberCount,
    int InvalidTaxIdCount,
    int NameConflictCount,
    IReadOnlyCollection<CompanyCatalogImportCompanyResponse> Companies,
    IReadOnlyCollection<CompanyCatalogImportWarningResponse> Warnings);

public sealed record CompanyCatalogImportCompanyResponse(
    string TaxId,
    string FormattedTaxId,
    string EvoName,
    int MemberCount,
    IReadOnlyCollection<CompanyMemberResponse> Members);

public sealed record CompanyCatalogImportWarningResponse(
    int? SourceRowNumber,
    string Code,
    string Message)
{
    public static CompanyCatalogImportWarningResponse FromDomain(CompanyCatalogWarning warning)
    {
        return new CompanyCatalogImportWarningResponse(
            warning.SourceRowNumber,
            warning.Code,
            warning.Message);
    }
}

/// <summary>Resultado da sincronização confirmada pelo operador.</summary>
public sealed record CompanyCatalogImportResponse(
    Guid ImportId,
    DateTimeOffset SynchronizedAt,
    string OperatorId,
    int CreatedCompanyCount,
    int UpdatedCompanyCount,

    /// <summary>Empresas atualizadas cujo nome operacional editado foi mantido.</summary>
    int PreservedCompanyCount,

    /// <summary>Empresas do catálogo que não apareceram nesta planilha.</summary>
    int UnseenCompanyCount,
    int RegistryEnrichedCount,
    int RegistryUnavailableCount,
    CompanyCatalogImportPreviewResponse Preview);

/// <summary>Última sincronização do catálogo, exibida na tela de integrações.</summary>
public sealed record CompanyCatalogImportSummaryResponse(
    Guid ImportId,
    string FileName,
    string OperatorId,
    DateTimeOffset SynchronizedAt,
    int AnalyzedRowCount,
    int DiscoveredCompanyCount,
    int CreatedCompanyCount,
    int UpdatedCompanyCount,
    int UnseenCompanyCount,
    int WarningCount)
{
    public static CompanyCatalogImportSummaryResponse FromDomain(CompanyCatalogImport companyCatalogImport)
    {
        return new CompanyCatalogImportSummaryResponse(
            companyCatalogImport.Id,
            companyCatalogImport.FileName,
            companyCatalogImport.OperatorId,
            companyCatalogImport.SynchronizedAt,
            companyCatalogImport.AnalyzedRowCount,
            companyCatalogImport.DiscoveredCompanyCount,
            companyCatalogImport.CreatedCompanyCount,
            companyCatalogImport.UpdatedCompanyCount,
            companyCatalogImport.UnseenCompanyCount,
            companyCatalogImport.WarningCount);
    }
}
