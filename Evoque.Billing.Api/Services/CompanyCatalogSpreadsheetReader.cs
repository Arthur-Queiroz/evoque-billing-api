using System.Text.RegularExpressions;
using System.Globalization;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Lê a exportação completa do CRM 2.0 do EVO para descobrir empresas pagadoras.
///
/// Este fluxo é diferente da leitura de fechamento: aqui o objetivo é descobrir
/// `nome no EVO + CNPJ + pessoas encontradas`. Uma linha com valor de contrato
/// vazio, zero ou inválido continua descobrindo a empresa, porque valor é regra
/// da prévia financeira e não do catálogo. Nenhuma prévia ou cobrança é criada.
/// </summary>
public sealed partial class CompanyCatalogSpreadsheetReader(SpreadsheetWorkbookReader spreadsheetWorkbookReader)
{
    private const int EmptyRowsBeforeEnd = 20;

    public ImportedCompanyCatalog Read(Stream spreadsheetStream, string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("O arquivo do catálogo deve estar no formato .xlsx.");
        }

        CompanyCatalogColumns? columns = null;
        var discoveredCompanies = new Dictionary<string, DiscoveredCompanyBuilder>(StringComparer.Ordinal);
        var warnings = new List<CompanyCatalogWarning>();
        var analyzedRowCount = 0;
        var duplicateMemberCount = 0;
        var consecutiveEmptyRows = 0;

        foreach (var row in spreadsheetWorkbookReader.ReadFirstWorksheetRows(spreadsheetStream, fileName))
        {
            if (columns is null)
            {
                columns = CompanyCatalogColumns.TryRead(row);
                continue;
            }

            var companyValue = row.ReadColumn(columns.CompanyColumn);
            var memberName = row.ReadColumn(columns.MemberNameColumn);
            if (string.IsNullOrWhiteSpace(companyValue) && string.IsNullOrWhiteSpace(memberName))
            {
                consecutiveEmptyRows++;
                if (consecutiveEmptyRows >= EmptyRowsBeforeEnd)
                {
                    break;
                }

                continue;
            }

            consecutiveEmptyRows = 0;
            analyzedRowCount++;

            if (!TryReadCompany(companyValue, out var evoName, out var taxId, out var warningCode))
            {
                warnings.Add(CreateCompanyWarning(row.RowNumber, warningCode));
                continue;
            }

            if (!discoveredCompanies.TryGetValue(taxId, out var companyBuilder))
            {
                companyBuilder = new DiscoveredCompanyBuilder(taxId);
                discoveredCompanies.Add(taxId, companyBuilder);
            }

            companyBuilder.RegisterObservedName(evoName);
            if (string.IsNullOrWhiteSpace(memberName))
            {
                continue;
            }

            var evoMemberIdValue = row.ReadColumn(columns.MemberIdColumn);
            if (!long.TryParse(
                    evoMemberIdValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var evoMemberId)
                || evoMemberId <= 0)
            {
                warnings.Add(new CompanyCatalogWarning(
                    row.RowNumber,
                    "InvalidMemberId",
                    "A linha possui empresa, mas não possui um IdCliente válido."));
                continue;
            }

            var contractName = row.ReadColumn(columns.ContractColumn);
            var evoContractId = row.ReadColumn(columns.ContractIdColumn);
            var memberWasAdded = companyBuilder.TryAddMember(
                row.RowNumber,
                evoMemberId,
                memberName.Trim(),
                string.IsNullOrWhiteSpace(evoContractId) ? null : evoContractId.Trim(),
                string.IsNullOrWhiteSpace(contractName) ? null : contractName.Trim());
            if (!memberWasAdded)
            {
                duplicateMemberCount++;
            }
        }

        if (columns is null)
        {
            throw new ValidationException(
                "Não foi encontrada a coluna Empresa ou Profissão na planilha exportada do EVO.");
        }

        if (columns.MemberNameColumn is null || columns.MemberIdColumn is null)
        {
            throw new ValidationException(
                "A planilha deve conter as colunas Nome e IdCliente para atualizar os colaboradores.");
        }

        var companies = discoveredCompanies.Values
            .Select(companyBuilder => companyBuilder.Build(warnings))
            .OrderBy(company => company.EvoName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(company => company.TaxId, StringComparer.Ordinal)
            .ToArray();

        return new ImportedCompanyCatalog(
            fileName,
            companies,
            analyzedRowCount,
            duplicateMemberCount,
            warnings);
    }

    private static CompanyCatalogWarning CreateCompanyWarning(int rowNumber, string warningCode)
    {
        var message = warningCode switch
        {
            "InvalidTaxId" =>
                "A linha possui um CNPJ com 14 dígitos, mas os dígitos verificadores são inválidos.",
            "MissingCompanyName" =>
                "A linha possui CNPJ, mas não possui o nome da empresa antes dele.",
            _ =>
                "A linha não possui empresa e CNPJ no formato esperado.",
        };
        return new CompanyCatalogWarning(rowNumber, warningCode, message);
    }

    /// <summary>
    /// Extrai `nome da empresa` e `CNPJ` do texto da coluna Empresa/Profissão,
    /// aceitando o CNPJ com ou sem formatação no final do valor.
    /// </summary>
    private static bool TryReadCompany(
        string companyValue,
        out string evoName,
        out string taxId,
        out string warningCode)
    {
        evoName = string.Empty;
        taxId = string.Empty;

        var taxIdMatch = TaxIdAtEndRegex().Match(companyValue);
        if (!taxIdMatch.Success)
        {
            warningCode = "MissingCompanyTaxId";
            return false;
        }

        if (!CompanyTaxId.TryNormalize(taxIdMatch.Groups["taxId"].Value, out var normalizedTaxId))
        {
            warningCode = "InvalidTaxId";
            return false;
        }

        var observedName = companyValue[..taxIdMatch.Index].Trim().TrimEnd('-', '–', ',').Trim();
        if (string.IsNullOrWhiteSpace(observedName))
        {
            warningCode = "MissingCompanyName";
            return false;
        }

        evoName = observedName;
        taxId = normalizedTaxId;
        warningCode = string.Empty;
        return true;
    }

    [GeneratedRegex(
        @"(?<!\d)(?<taxId>\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdAtEndRegex();

    /// <summary>
    /// Acumula as observações de um mesmo CNPJ enquanto a planilha é lida. Um
    /// CNPJ nunca vira duas empresas: nomes divergentes são resolvidos no fim.
    /// </summary>
    private sealed class DiscoveredCompanyBuilder(string taxId)
    {
        private readonly Dictionary<string, int> observationCountByName = new(StringComparer.Ordinal);
        private readonly Dictionary<long, ImportedCatalogMemberBuilder> membersById = [];

        public void RegisterObservedName(string evoName)
        {
            observationCountByName[evoName] = observationCountByName.GetValueOrDefault(evoName) + 1;
        }

        public bool TryAddMember(
            int sourceRowNumber,
            long evoMemberId,
            string memberName,
            string? evoContractId,
            string? contractName)
        {
            if (!membersById.TryGetValue(evoMemberId, out var memberBuilder))
            {
                memberBuilder = new ImportedCatalogMemberBuilder(
                    sourceRowNumber,
                    evoMemberId,
                    memberName);
                membersById.Add(evoMemberId, memberBuilder);
            }

            return memberBuilder.TryAddContract(evoContractId, contractName);
        }

        public ImportedCatalogCompany Build(List<CompanyCatalogWarning> warnings)
        {
            var chosenName = ChooseMostFrequentName();
            if (observationCountByName.Count > 1)
            {
                var conflictingNames = observationCountByName.Keys
                    .Where(observedName => observedName != chosenName)
                    .OrderBy(observedName => observedName, StringComparer.Ordinal);
                warnings.Add(new CompanyCatalogWarning(
                    null,
                    "CompanyNameConflict",
                    $"O CNPJ {CompanyTaxId.Format(taxId)} apareceu com nomes diferentes. "
                    + $"Foi usado \"{chosenName}\"; também apareceu como "
                    + $"{string.Join(", ", conflictingNames.Select(name => $"\"{name}\""))}."));
            }

            return new ImportedCatalogCompany(
                taxId,
                chosenName,
                membersById.Values
                    .Select(member => member.Build())
                    .OrderBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(member => member.SourceRowNumber)
                    .ToArray());
        }

        /// <summary>
        /// O nome mais observado vence. Empates são desempatados pela ordem
        /// alfabética para que a mesma planilha produza sempre o mesmo resultado.
        /// </summary>
        private string ChooseMostFrequentName()
        {
            return observationCountByName
                .OrderByDescending(observation => observation.Value)
                .ThenBy(observation => observation.Key, StringComparer.Ordinal)
                .First()
                .Key;
        }

        private sealed class ImportedCatalogMemberBuilder(
            int sourceRowNumber,
            long evoMemberId,
            string memberName)
        {
            private readonly Dictionary<string, ImportedCatalogContract> contractsByKey =
                new(StringComparer.Ordinal);

            public bool TryAddContract(string? evoContractId, string? contractName)
            {
                var contractKey = CreateContractKey(evoContractId, contractName);
                return contractsByKey.TryAdd(
                    contractKey,
                    new ImportedCatalogContract(contractKey, evoContractId, contractName));
            }

            public ImportedCatalogMember Build()
            {
                return new ImportedCatalogMember(
                    sourceRowNumber,
                    evoMemberId,
                    memberName,
                    contractsByKey.Values
                        .OrderBy(contract => contract.ContractName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(contract => contract.ContractKey, StringComparer.Ordinal)
                        .ToArray());
            }

            private static string CreateContractKey(string? evoContractId, string? contractName)
            {
                if (!string.IsNullOrWhiteSpace(evoContractId))
                {
                    return $"id:{evoContractId.Trim()}";
                }

                var normalizedContractName = SpreadsheetText.Normalize(contractName ?? string.Empty);
                return $"name:{normalizedContractName}";
            }
        }
    }
}

/// <summary>Colunas reconhecidas na exportação completa do EVO.</summary>
public sealed record CompanyCatalogColumns(
    string CompanyColumn,
    string? MemberNameColumn,
    string? MemberIdColumn,
    string? ContractColumn,
    string? ContractIdColumn)
{
    /// <summary>
    /// Só a coluna de empresa é obrigatória: sem ela não há como descobrir o
    /// CNPJ. Nome e contrato enriquecem o retrato de pessoas quando existem.
    /// </summary>
    public static CompanyCatalogColumns? TryRead(SpreadsheetRow headerRow)
    {
        string? companyColumn = null;
        string? memberNameColumn = null;
        string? memberIdColumn = null;
        string? contractColumn = null;
        string? contractIdColumn = null;

        foreach (var cellValue in headerRow.CellValuesByColumn)
        {
            var normalizedHeader = SpreadsheetText.Normalize(cellValue.Value);
            if (normalizedHeader is "empresa" or "profissao")
            {
                companyColumn = cellValue.Key;
            }
            else if (normalizedHeader == "nome")
            {
                memberNameColumn = cellValue.Key;
            }
            else if (normalizedHeader == "idcliente")
            {
                memberIdColumn = cellValue.Key;
            }
            else if (normalizedHeader == "contrato")
            {
                contractColumn = cellValue.Key;
            }
            else if (normalizedHeader == "idcontrato")
            {
                contractIdColumn = cellValue.Key;
            }
        }

        return companyColumn is null
            ? null
            : new CompanyCatalogColumns(
                companyColumn,
                memberNameColumn,
                memberIdColumn,
                contractColumn,
                contractIdColumn);
    }
}

public sealed record ImportedCompanyCatalog(
    string FileName,
    IReadOnlyCollection<ImportedCatalogCompany> Companies,
    int AnalyzedRowCount,
    int DuplicateMemberCount,
    IReadOnlyCollection<CompanyCatalogWarning> Warnings);

public sealed record ImportedCatalogCompany(
    string TaxId,
    string EvoName,
    IReadOnlyCollection<ImportedCatalogMember> Members);

public sealed record ImportedCatalogMember(
    int SourceRowNumber,
    long EvoMemberId,
    string MemberName,
    IReadOnlyCollection<ImportedCatalogContract> Contracts);

public sealed record ImportedCatalogContract(
    string ContractKey,
    string? EvoContractId,
    string? ContractName);

public sealed record CompanyCatalogWarning(
    int? SourceRowNumber,
    string Code,
    string Message);
