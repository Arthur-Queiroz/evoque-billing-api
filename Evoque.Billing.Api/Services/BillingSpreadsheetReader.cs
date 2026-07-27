using System.Globalization;
using System.Text.RegularExpressions;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Lê a planilha de fechamento validada e produz as linhas que podem virar uma
/// prévia financeira. Uma linha só é aceita quando possui empresa com CNPJ,
/// pessoa e valor de contrato maior que zero.
/// </summary>
public sealed partial class BillingSpreadsheetReader(SpreadsheetWorkbookReader spreadsheetWorkbookReader)
{
    private const int MaximumImportedRows = 5000;
    private const int EmptyRowsBeforeEnd = 20;

    public ImportedBillingSpreadsheet Read(Stream spreadsheetStream, string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("O arquivo de faturamento deve estar no formato .xlsx.");
        }

        BillingSpreadsheetColumns? columns = null;
        var importedRows = new List<ImportedBillingSpreadsheetRow>();
        var warnings = new List<ImportedBillingSpreadsheetWarning>();
        var seenRowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateRowCount = 0;
        var consecutiveEmptyRows = 0;

        foreach (var row in spreadsheetWorkbookReader.ReadFirstWorksheetRows(spreadsheetStream, fileName))
        {
            if (columns is null)
            {
                columns = BillingSpreadsheetColumns.TryRead(row);
                continue;
            }

            var memberName = row.ReadColumn(columns.MemberNameColumn);
            var companyValue = row.ReadColumn(columns.CompanyColumn);
            var amountValue = row.ReadColumn(columns.AmountColumn);
            if (string.IsNullOrWhiteSpace(memberName)
                && string.IsNullOrWhiteSpace(companyValue)
                && string.IsNullOrWhiteSpace(amountValue))
            {
                consecutiveEmptyRows++;
                if (consecutiveEmptyRows >= EmptyRowsBeforeEnd)
                {
                    break;
                }

                continue;
            }

            consecutiveEmptyRows = 0;
            if (!TryReadCompany(companyValue, out var companyName, out var companyTaxId))
            {
                warnings.Add(new ImportedBillingSpreadsheetWarning(
                    row.RowNumber,
                    "InvalidCompany",
                    "A linha não possui empresa e CNPJ no formato esperado."));
                continue;
            }

            if (!TryReadAmount(amountValue, out var amount) || amount <= 0)
            {
                warnings.Add(new ImportedBillingSpreadsheetWarning(
                    row.RowNumber,
                    "InvalidAmount",
                    "A linha não possui um valor de contrato válido e maior que zero."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(memberName))
            {
                warnings.Add(new ImportedBillingSpreadsheetWarning(
                    row.RowNumber,
                    "MissingMemberName",
                    "A linha não possui o nome da pessoa faturada."));
                continue;
            }

            var contractName = row.ReadColumn(columns.ContractColumn);
            var importedRow = new ImportedBillingSpreadsheetRow(
                row.RowNumber,
                memberName.Trim(),
                string.IsNullOrWhiteSpace(contractName) ? "Contrato corporativo" : contractName.Trim(),
                companyName,
                companyTaxId,
                decimal.Round(amount, 2, MidpointRounding.AwayFromZero));
            var duplicateKey = CreateDuplicateKey(importedRow);
            if (!seenRowKeys.Add(duplicateKey))
            {
                duplicateRowCount++;
                continue;
            }

            importedRows.Add(importedRow);
            if (importedRows.Count >= MaximumImportedRows)
            {
                warnings.Add(new ImportedBillingSpreadsheetWarning(
                    null,
                    "MaximumRowsReached",
                    $"A importação foi limitada a {MaximumImportedRows} linhas."));
                break;
            }
        }

        if (columns is null)
        {
            throw new ValidationException(
                "Não foram encontradas as colunas Nome, Empresa/Profissão e Valor do contrato.");
        }

        if (importedRows.Count == 0)
        {
            throw new ValidationException("A planilha não possui nenhuma linha válida para faturamento.");
        }

        return new ImportedBillingSpreadsheet(
            fileName,
            importedRows,
            duplicateRowCount,
            warnings);
    }

    private static bool TryReadCompany(
        string companyValue,
        out string companyName,
        out string companyTaxId)
    {
        var taxIdMatch = TaxIdAtEndRegex().Match(companyValue);
        if (!taxIdMatch.Success)
        {
            companyName = string.Empty;
            companyTaxId = string.Empty;
            return false;
        }

        companyTaxId = taxIdMatch.Groups["taxId"].Value;
        companyName = companyValue[..taxIdMatch.Index].Trim().TrimEnd('-').Trim();
        return !string.IsNullOrWhiteSpace(companyName);
    }

    private static bool TryReadAmount(string amountValue, out decimal amount)
    {
        return decimal.TryParse(
                amountValue,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount)
            || decimal.TryParse(
                amountValue,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.GetCultureInfo("pt-BR"),
                out amount);
    }

    private static string CreateDuplicateKey(ImportedBillingSpreadsheetRow row)
    {
        return string.Join(
            "|",
            SpreadsheetText.Normalize(row.MemberName),
            SpreadsheetText.Normalize(row.ContractName),
            row.CompanyTaxId,
            row.Amount.ToString("0.00", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"(?<taxId>\d{14})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdAtEndRegex();
}

/// <summary>Colunas reconhecidas no cabeçalho da planilha de fechamento.</summary>
public sealed record BillingSpreadsheetColumns(
    string MemberNameColumn,
    string CompanyColumn,
    string AmountColumn,
    string? ContractColumn)
{
    public static BillingSpreadsheetColumns? TryRead(SpreadsheetRow headerRow)
    {
        string? memberNameColumn = null;
        string? companyColumn = null;
        string? amountColumn = null;
        string? contractColumn = null;

        foreach (var cellValue in headerRow.CellValuesByColumn)
        {
            var normalizedHeader = SpreadsheetText.Normalize(cellValue.Value);
            if (normalizedHeader == "nome")
            {
                memberNameColumn = cellValue.Key;
            }
            else if (normalizedHeader is "empresa" or "profissao")
            {
                companyColumn = cellValue.Key;
            }
            else if (normalizedHeader == "valor do contrato")
            {
                amountColumn = cellValue.Key;
            }
            else if (normalizedHeader == "contrato")
            {
                contractColumn = cellValue.Key;
            }
        }

        if (memberNameColumn is null || companyColumn is null || amountColumn is null)
        {
            return null;
        }

        return new BillingSpreadsheetColumns(
            memberNameColumn,
            companyColumn,
            amountColumn,
            contractColumn);
    }
}

public sealed record ImportedBillingSpreadsheet(
    string FileName,
    IReadOnlyCollection<ImportedBillingSpreadsheetRow> Rows,
    int DuplicateRowCount,
    IReadOnlyCollection<ImportedBillingSpreadsheetWarning> Warnings);

public sealed record ImportedBillingSpreadsheetRow(
    int SourceRowNumber,
    string MemberName,
    string ContractName,
    string CompanyName,
    string CompanyTaxId,
    decimal Amount);

public sealed record ImportedBillingSpreadsheetWarning(
    int? SourceRowNumber,
    string Code,
    string Message);
