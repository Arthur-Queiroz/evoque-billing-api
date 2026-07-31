using System.Security.Cryptography;
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Vincula colaboradores da exportação completa do CRM 2.0 do EVO às empresas
/// já cadastradas no catálogo.
///
/// Esta importação <b>não cria empresa</b>. A coluna Profissão do EVO contém o
/// empregador da pessoa, não a empresa que paga: tratá-la como empresa pagadora
/// cadastrou sindicatos, igrejas e planos internos como se fossem clientes. O
/// catálogo é mantido à mão e um CNPJ desconhecido vira pendência, nunca um
/// cadastro novo.
///
/// O fluxo é: upload → preview → confirmação do operador → vínculo dos
/// colaboradores. Nenhuma etapa cria prévia financeira nem chama o Asaas.
/// </summary>
public sealed class CompanyCatalogImportService(
    CompanyCatalogSpreadsheetReader companyCatalogSpreadsheetReader,
    ICompanyRepository companyRepository,
    ICompanyCatalogImportRepository companyCatalogImportRepository,
    CorporateMemberService corporateMemberService)
{
    private const long MaximumSpreadsheetSize = 25 * 1024 * 1024;

    public async Task<CompanyCatalogImportPreviewResponse> PreviewAsync(
        IFormFile spreadsheetFile,
        CancellationToken cancellationToken)
    {
        var spreadsheetContent = await ReadSpreadsheetContentAsync(spreadsheetFile, cancellationToken);
        var importedCatalog = ReadCatalog(spreadsheetContent, spreadsheetFile.FileName);
        var existingCompanies = await companyRepository.ListAsync(cancellationToken);
        var existingTaxIds = existingCompanies
            .Select(company => company.TaxId)
            .ToHashSet(StringComparer.Ordinal);
        var memberComparison = await corporateMemberService.CompareAsync(
            importedCatalog,
            cancellationToken);
        return CreatePreviewResponse(importedCatalog, existingTaxIds, memberComparison);
    }

    public async Task<CompanyCatalogImportResponse> SynchronizeAsync(
        IFormFile spreadsheetFile,
        string operatorId,
        bool completeSnapshotConfirmed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela sincronização do catálogo é obrigatório.");
        }

        if (!completeSnapshotConfirmed)
        {
            throw new ValidationException(
                "Confirme que o arquivo contém a exportação completa de clientes ativos do CRM 2.0.");
        }

        var spreadsheetContent = await ReadSpreadsheetContentAsync(spreadsheetFile, cancellationToken);
        var importedCatalog = ReadCatalog(spreadsheetContent, spreadsheetFile.FileName);

        var importId = Guid.NewGuid();
        var synchronizedAt = DateTimeOffset.UtcNow;
        var existingCompanies = await companyRepository.ListAsync(cancellationToken);
        var existingTaxIds = existingCompanies
            .Select(company => company.TaxId)
            .ToHashSet(StringComparer.Ordinal);
        var previewMemberComparison = await corporateMemberService.CompareAsync(
            importedCatalog,
            cancellationToken);
        if (previewMemberComparison.ConflictMemberCount > 0)
        {
            throw new ConflictException(
                $"A planilha possui {previewMemberComparison.ConflictMemberCount} colaborador(es) "
                + "com divergência de empresa. Nenhum vínculo foi alterado.");
        }

        var registeredCompanyCount = importedCatalog.Companies
            .Count(discoveredCompany => existingTaxIds.Contains(discoveredCompany.TaxId));
        var unregisteredCompanies = importedCatalog.Companies
            .Where(discoveredCompany => !existingTaxIds.Contains(discoveredCompany.TaxId))
            .OrderByDescending(discoveredCompany => discoveredCompany.Members.Count)
            .Select(discoveredCompany => new UnregisteredCompanyResponse(
                discoveredCompany.TaxId,
                CompanyTaxId.Format(discoveredCompany.TaxId),
                discoveredCompany.EvoName,
                discoveredCompany.Members.Count))
            .ToArray();

        var importedMembers = importedCatalog.Companies
            .SelectMany(company => company.Members.Select(member => new CompanyCatalogImportMember(
                importId,
                company.TaxId,
                member.SourceRowNumber,
                member.MemberName,
                member.Contracts.FirstOrDefault()?.ContractName)))
            .ToArray();

        var companyCatalogImport = CompanyCatalogImport.Create(
            importId,
            importedCatalog.FileName,
            ComputeFileHash(spreadsheetContent),
            operatorId,
            synchronizedAt,
            importedCatalog.AnalyzedRowCount,
            importedCatalog.Companies.Count,
            createdCompanyCount: 0,
            updatedCompanyCount: 0,
            unseenCompanyCount: 0,
            importedCatalog.Warnings.Count);
        var auditLog = AuditLog.Create(
            "company-catalog.members-linked",
            operatorId,
            synchronizedAt,
            null,
            null,
            $"Vínculo de colaboradores {importId}: {importedCatalog.Companies.Count} empresas na planilha, "
            + $"{registeredCompanyCount} cadastradas e vinculadas, "
            + $"{unregisteredCompanies.Length} fora do catálogo e ignoradas, "
            + $"{importedCatalog.Warnings.Count} avisos. Nenhuma empresa foi criada.");
        await companyCatalogImportRepository.AddAsync(
            companyCatalogImport,
            importedMembers,
            Array.Empty<Company>(),
            auditLog,
            cancellationToken);

        var memberComparison = await corporateMemberService.ApplyCompleteSnapshotAsync(
            importedCatalog,
            importId,
            operatorId,
            synchronizedAt,
            completeSnapshotConfirmed,
            cancellationToken);

        return new CompanyCatalogImportResponse(
            importId,
            synchronizedAt,
            companyCatalogImport.OperatorId,
            registeredCompanyCount,
            unregisteredCompanies,
            memberComparison,
            CreatePreviewResponse(importedCatalog, existingTaxIds, memberComparison));
    }

    public Task<CompanyCatalogImportResponse> SynchronizeAsync(
        IFormFile spreadsheetFile,
        string operatorId,
        CancellationToken cancellationToken)
    {
        return SynchronizeAsync(
            spreadsheetFile,
            operatorId,
            completeSnapshotConfirmed: true,
            cancellationToken);
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
        ImportedCompanyCatalog importedCatalog,
        IReadOnlySet<string> existingTaxIds,
        CorporateMemberComparisonResponse memberComparison)
    {
        var companies = importedCatalog.Companies
            .Select(company => new CompanyCatalogImportCompanyResponse(
                company.TaxId,
                CompanyTaxId.Format(company.TaxId),
                company.EvoName,
                existingTaxIds.Contains(company.TaxId),
                company.Members.Count,
                company.Members
                    .Select(member => new CompanyCatalogImportedMemberResponse(
                        member.EvoMemberId,
                        member.MemberName,
                        member.Contracts
                            .Select(contract => contract.ContractName)
                            .Where(contractName => !string.IsNullOrWhiteSpace(contractName))
                            .Cast<string>()
                            .ToArray(),
                        member.SourceRowNumber))
                    .ToArray()))
            .ToArray();

        return new CompanyCatalogImportPreviewResponse(
            importedCatalog.FileName,
            importedCatalog.AnalyzedRowCount,
            companies.Length,
            companies.Count(company => !company.IsAlreadyRegistered),
            companies.Count(company => company.IsAlreadyRegistered),
            companies.Sum(company => company.MemberCount),
            importedCatalog.DuplicateMemberCount,
            importedCatalog.Warnings.Count(warning => warning.Code == "InvalidTaxId"),
            importedCatalog.Warnings.Count(warning => warning.Code == "CompanyNameConflict"),
            memberComparison,
            companies,
            importedCatalog.Warnings
                .Select(CompanyCatalogImportWarningResponse.FromDomain)
                .ToArray());
    }
}
