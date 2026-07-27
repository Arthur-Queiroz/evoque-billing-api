using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

public sealed class BillingSpreadsheetImportService(
    BillingSpreadsheetReader billingSpreadsheetReader,
    BillingDraftService billingDraftService)
{
    private const long MaximumSpreadsheetSize = 25 * 1024 * 1024;

    public async Task<BillingSpreadsheetPreviewResponse> PreviewAsync(
        IFormFile spreadsheetFile,
        CancellationToken cancellationToken)
    {
        var importedSpreadsheet = await ReadSpreadsheetAsync(spreadsheetFile, cancellationToken);
        return CreatePreviewResponse(importedSpreadsheet);
    }

    public async Task<BillingSpreadsheetDraftImportResponse> CreateDraftsAsync(
        BillingPeriodReference billingPeriodReference,
        IFormFile spreadsheetFile,
        string operatorId,
        string? asaasCustomerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela importação é obrigatório.");
        }

        var importedSpreadsheet = await ReadSpreadsheetAsync(spreadsheetFile, cancellationToken);
        var companyGroups = importedSpreadsheet.Rows
            .GroupBy(row => row.CompanyTaxId)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(asaasCustomerId) && companyGroups.Length > 1)
        {
            throw new ValidationException(
                "Um único cliente Asaas só pode ser associado a uma planilha com uma empresa.");
        }

        var billingDrafts = new List<BillingDraft>();
        foreach (var companyGroup in companyGroups)
        {
            var firstCompanyRow = companyGroup.First();
            var command = new CreateBillingDraftCommand(
                firstCompanyRow.CompanyTaxId,
                firstCompanyRow.CompanyName,
                firstCompanyRow.CompanyTaxId,
                string.IsNullOrWhiteSpace(asaasCustomerId) ? null : asaasCustomerId.Trim(),
                companyGroup
                    .OrderBy(row => row.MemberName)
                    .Select(row => new CreateBillingDraftItemCommand(
                        $"{row.MemberName} — {row.ContractName}",
                        1,
                        row.Amount,
                        null))
                    .ToArray());
            var billingDraft = await billingDraftService.CreateAsync(
                billingPeriodReference,
                command,
                operatorId,
                cancellationToken);
            billingDrafts.Add(billingDraft);
        }

        return new BillingSpreadsheetDraftImportResponse(
            CreatePreviewResponse(importedSpreadsheet),
            billingDrafts.Select(BillingDraftResponse.FromDomain).ToArray());
    }

    private async Task<ImportedBillingSpreadsheet> ReadSpreadsheetAsync(
        IFormFile spreadsheetFile,
        CancellationToken cancellationToken)
    {
        if (spreadsheetFile.Length == 0)
        {
            throw new ValidationException("A planilha de faturamento está vazia.");
        }

        if (spreadsheetFile.Length > MaximumSpreadsheetSize)
        {
            throw new ValidationException("A planilha de faturamento deve ter no máximo 25 MB.");
        }

        await using var spreadsheetStream = new MemoryStream();
        await spreadsheetFile.CopyToAsync(spreadsheetStream, cancellationToken);
        spreadsheetStream.Position = 0;
        return billingSpreadsheetReader.Read(spreadsheetStream, spreadsheetFile.FileName);
    }

    private static BillingSpreadsheetPreviewResponse CreatePreviewResponse(
        ImportedBillingSpreadsheet importedSpreadsheet)
    {
        var companies = importedSpreadsheet.Rows
            .GroupBy(row => row.CompanyTaxId)
            .Select(companyGroup =>
            {
                var firstCompanyRow = companyGroup.First();
                var members = companyGroup
                    .OrderBy(row => row.MemberName)
                    .Select(row => new BillingSpreadsheetMemberResponse(
                        row.MemberName,
                        row.ContractName,
                        row.Amount,
                        row.SourceRowNumber))
                    .ToArray();
                return new BillingSpreadsheetCompanyResponse(
                    firstCompanyRow.CompanyName,
                    firstCompanyRow.CompanyTaxId,
                    members.Length,
                    members.Sum(member => member.Amount),
                    members);
            })
            .OrderBy(company => company.CompanyName)
            .ToArray();

        return new BillingSpreadsheetPreviewResponse(
            importedSpreadsheet.FileName,
            importedSpreadsheet.Rows.Count,
            importedSpreadsheet.DuplicateRowCount,
            companies.Sum(company => company.TotalAmount),
            companies,
            importedSpreadsheet.Warnings
                .Select(warning => new BillingSpreadsheetWarningResponse(
                    warning.SourceRowNumber,
                    warning.Code,
                    warning.Message))
                .ToArray());
    }
}
