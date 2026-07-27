using System.Security.Cryptography;
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Sincroniza o catálogo interno a partir da exportação completa do CRM 2.0 do
/// EVO.
///
/// O fluxo é: upload → preview → confirmação do operador → upsert por CNPJ →
/// enriquecimento cadastral tolerante a falhas. Nenhuma etapa cria prévia
/// financeira nem chama o Asaas.
/// </summary>
public sealed class CompanyCatalogImportService(
    CompanyCatalogSpreadsheetReader companyCatalogSpreadsheetReader,
    ICompanyRepository companyRepository,
    ICompanyCatalogImportRepository companyCatalogImportRepository,
    CompanyRegistryEnrichmentService companyRegistryEnrichmentService)
{
    private const long MaximumSpreadsheetSize = 25 * 1024 * 1024;

    public async Task<CompanyCatalogImportPreviewResponse> PreviewAsync(
        IFormFile spreadsheetFile,
        CancellationToken cancellationToken)
    {
        var spreadsheetContent = await ReadSpreadsheetContentAsync(spreadsheetFile, cancellationToken);
        var importedCatalog = ReadCatalog(spreadsheetContent, spreadsheetFile.FileName);
        return CreatePreviewResponse(importedCatalog);
    }

    public async Task<CompanyCatalogImportResponse> SynchronizeAsync(
        IFormFile spreadsheetFile,
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela sincronização do catálogo é obrigatório.");
        }

        var spreadsheetContent = await ReadSpreadsheetContentAsync(spreadsheetFile, cancellationToken);
        var importedCatalog = ReadCatalog(spreadsheetContent, spreadsheetFile.FileName);

        var importId = Guid.NewGuid();
        var synchronizedAt = DateTimeOffset.UtcNow;
        var existingCompanies = await companyRepository.ListAsync(cancellationToken);
        var existingCompaniesByTaxId = existingCompanies.ToDictionary(
            company => company.TaxId,
            StringComparer.Ordinal);

        var createdCompanies = new List<Company>();
        var synchronizedCompanies = new List<Company>();
        var updatedCompanyCount = 0;
        var preservedManualNameCount = 0;
        foreach (var discoveredCompany in importedCatalog.Companies)
        {
            if (existingCompaniesByTaxId.TryGetValue(discoveredCompany.TaxId, out var existingCompany))
            {
                // Um nome operacional diferente do observado no EVO só existe
                // porque alguém o editou; contar antes deixa isso visível no
                // resultado da sincronização.
                if (!string.Equals(
                    existingCompany.DisplayName,
                    discoveredCompany.EvoName,
                    StringComparison.Ordinal))
                {
                    preservedManualNameCount++;
                }

                // Nome operacional, status, agenda e vínculos Asaas continuam
                // como estavam: a planilha só atualiza o que ela realmente observou.
                existingCompany.ApplyEvoSpreadsheetSynchronization(
                    discoveredCompany.EvoName,
                    discoveredCompany.Members.Count,
                    importId,
                    operatorId,
                    synchronizedAt);
                synchronizedCompanies.Add(existingCompany);
                updatedCompanyCount++;
                continue;
            }

            var newCompany = Company.CreateFromEvoSpreadsheet(
                discoveredCompany.TaxId,
                discoveredCompany.EvoName,
                discoveredCompany.Members.Count,
                importId,
                operatorId,
                synchronizedAt);
            synchronizedCompanies.Add(newCompany);
            createdCompanies.Add(newCompany);
        }

        // Empresas ausentes nesta planilha não são inativadas nem excluídas;
        // apenas deixam de constar como vistas na última sincronização.
        var unseenCompanyCount = existingCompanies.Count(company => !company.WasSeenInImport(importId));

        var importedMembers = importedCatalog.Companies
            .SelectMany(company => company.Members.Select(member => new CompanyCatalogImportMember(
                importId,
                company.TaxId,
                member.SourceRowNumber,
                member.MemberName,
                member.ContractName)))
            .ToArray();

        var companyCatalogImport = CompanyCatalogImport.Create(
            importId,
            importedCatalog.FileName,
            ComputeFileHash(spreadsheetContent),
            operatorId,
            synchronizedAt,
            importedCatalog.AnalyzedRowCount,
            importedCatalog.Companies.Count,
            createdCompanies.Count,
            updatedCompanyCount,
            unseenCompanyCount,
            importedCatalog.Warnings.Count);
        var auditLog = AuditLog.Create(
            "company-catalog.synchronized",
            operatorId,
            synchronizedAt,
            null,
            null,
            $"Sincronização {importId} do catálogo: {importedCatalog.Companies.Count} empresas "
            + $"encontradas, {createdCompanies.Count} criadas, {updatedCompanyCount} atualizadas, "
            + $"{unseenCompanyCount} não vistas, {importedCatalog.Warnings.Count} avisos.");
        await companyCatalogImportRepository.AddAsync(
            companyCatalogImport,
            importedMembers,
            synchronizedCompanies,
            auditLog,
            cancellationToken);

        // Só as empresas novas são consultadas automaticamente. As já existentes
        // mantêm o cadastro persistido até alguém pedir atualização explícita.
        var enrichmentSummary = await companyRegistryEnrichmentService.RefreshManyAsync(
            createdCompanies,
            cancellationToken);

        return new CompanyCatalogImportResponse(
            importId,
            synchronizedAt,
            companyCatalogImport.OperatorId,
            createdCompanies.Count,
            updatedCompanyCount,
            preservedManualNameCount,
            unseenCompanyCount,
            enrichmentSummary.EnrichedCount,
            enrichmentSummary.UnavailableCount,
            CreatePreviewResponse(importedCatalog));
    }

    private ImportedCompanyCatalog ReadCatalog(byte[] spreadsheetContent, string fileName)
    {
        using var spreadsheetStream = new MemoryStream(spreadsheetContent);
        return companyCatalogSpreadsheetReader.Read(spreadsheetStream, fileName);
    }

    private static async Task<byte[]> ReadSpreadsheetContentAsync(
        IFormFile spreadsheetFile,
        CancellationToken cancellationToken)
    {
        if (spreadsheetFile.Length == 0)
        {
            throw new ValidationException("A planilha do catálogo está vazia.");
        }

        if (spreadsheetFile.Length > MaximumSpreadsheetSize)
        {
            throw new ValidationException("A planilha do catálogo deve ter no máximo 25 MB.");
        }

        using var spreadsheetStream = new MemoryStream();
        await spreadsheetFile.CopyToAsync(spreadsheetStream, cancellationToken);
        return spreadsheetStream.ToArray();
    }

    private static string ComputeFileHash(byte[] spreadsheetContent)
    {
        return Convert.ToHexStringLower(SHA256.HashData(spreadsheetContent));
    }

    private static CompanyCatalogImportPreviewResponse CreatePreviewResponse(
        ImportedCompanyCatalog importedCatalog)
    {
        var companies = importedCatalog.Companies
            .Select(company => new CompanyCatalogImportCompanyResponse(
                company.TaxId,
                CompanyTaxId.Format(company.TaxId),
                company.EvoName,
                company.Members.Count,
                company.Members
                    .Select(member => new CompanyMemberResponse(
                        member.MemberName,
                        member.ContractName,
                        member.SourceRowNumber))
                    .ToArray()))
            .ToArray();

        return new CompanyCatalogImportPreviewResponse(
            importedCatalog.FileName,
            importedCatalog.AnalyzedRowCount,
            companies.Length,
            companies.Sum(company => company.MemberCount),
            importedCatalog.DuplicateMemberCount,
            importedCatalog.Warnings.Count(warning => warning.Code == "InvalidTaxId"),
            importedCatalog.Warnings.Count(warning => warning.Code == "CompanyNameConflict"),
            companies,
            importedCatalog.Warnings
                .Select(CompanyCatalogImportWarningResponse.FromDomain)
                .ToArray());
    }
}
