namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Uma execução da sincronização do catálogo a partir da planilha exportada do
/// EVO. Guardar a execução permite responder "esta empresa apareceu na última
/// sincronização?" sem inativar ninguém automaticamente.
/// </summary>
public sealed record CompanyCatalogImport(
    Guid Id,
    string FileName,
    string FileHash,
    string OperatorId,
    DateTimeOffset SynchronizedAt,
    int AnalyzedRowCount,
    int DiscoveredCompanyCount,
    int CreatedCompanyCount,
    int UpdatedCompanyCount,
    int UnseenCompanyCount,
    int WarningCount)
{
    public static CompanyCatalogImport Create(
        Guid id,
        string fileName,
        string fileHash,
        string operatorId,
        DateTimeOffset synchronizedAt,
        int analyzedRowCount,
        int discoveredCompanyCount,
        int createdCompanyCount,
        int updatedCompanyCount,
        int unseenCompanyCount,
        int warningCount)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela sincronização do catálogo é obrigatório.");
        }

        return new CompanyCatalogImport(
            id,
            fileName,
            fileHash,
            operatorId.Trim(),
            synchronizedAt,
            analyzedRowCount,
            discoveredCompanyCount,
            createdCompanyCount,
            updatedCompanyCount,
            unseenCompanyCount,
            warningCount);
    }
}

/// <summary>
/// Pessoa encontrada para uma empresa em uma sincronização. É um retrato da
/// planilha, não um vínculo financeiro: não gera prévia nem cobrança.
/// </summary>
public sealed record CompanyCatalogImportMember(
    Guid ImportId,
    string CompanyTaxId,
    int SourceRowNumber,
    string MemberName,
    string? ContractName);
