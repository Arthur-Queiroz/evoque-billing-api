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
    int NewCompanyCount,
    int ExistingCompanyCount,
    int DiscoveredMemberCount,
    int DuplicateMemberCount,
    int InvalidTaxIdCount,
    int NameConflictCount,
    CorporateMemberComparisonResponse MemberComparison,
    IReadOnlyCollection<CompanyCatalogImportCompanyResponse> Companies,
    IReadOnlyCollection<CompanyCatalogImportWarningResponse> Warnings);

public sealed record CompanyCatalogImportCompanyResponse(
    string TaxId,
    string FormattedTaxId,
    string EvoName,
    bool IsAlreadyRegistered,
    int MemberCount,
    IReadOnlyCollection<CompanyCatalogImportedMemberResponse> Members);

public sealed record CompanyCatalogImportedMemberResponse(
    long EvoMemberId,
    string MemberName,
    IReadOnlyCollection<string> Contracts,
    int SourceRowNumber);

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

/// <summary>
/// Empresa encontrada na planilha do EVO que não existe no catálogo. Ela não é
/// cadastrada: aparece aqui para alguém decidir se é cliente de verdade.
/// </summary>
public sealed record UnregisteredCompanyResponse(
    string TaxId,
    string FormattedTaxId,
    string EvoName,
    int MemberCount);

/// <summary>
/// Resultado do vínculo confirmado pelo operador. Nenhuma empresa é criada:
/// <paramref name="LinkedCompanyCount"/> conta as que já estavam cadastradas e
/// receberam colaboradores; <paramref name="UnregisteredCompanies"/> lista os
/// CNPJs da planilha que ficaram de fora.
/// </summary>
public sealed record CompanyCatalogImportResponse(
    Guid ImportId,
    DateTimeOffset SynchronizedAt,
    string OperatorId,
    int LinkedCompanyCount,
    IReadOnlyCollection<UnregisteredCompanyResponse> UnregisteredCompanies,
    CorporateMemberComparisonResponse MemberComparison,
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
    int IgnoredExistingCompanyCount,
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
            Math.Max(
                0,
                companyCatalogImport.DiscoveredCompanyCount
                - companyCatalogImport.CreatedCompanyCount),
            companyCatalogImport.WarningCount);
    }
}
