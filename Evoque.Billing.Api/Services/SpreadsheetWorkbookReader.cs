using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Lê a estrutura de um arquivo XLSX e devolve as linhas da primeira aba já
/// resolvidas em texto. Esta classe não conhece nenhuma regra de faturamento ou
/// de catálogo: ela existe apenas para que os dois fluxos compartilhem o mesmo
/// parsing de pacote OpenXML.
/// </summary>
public sealed partial class SpreadsheetWorkbookReader
{
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Percorre a primeira aba da planilha de forma preguiçosa. A enumeração
    /// mantém o pacote aberto até o consumidor terminar de ler, para que
    /// planilhas grandes não precisem ser materializadas por inteiro.
    /// </summary>
    public IEnumerable<SpreadsheetRow> ReadFirstWorksheetRows(Stream spreadsheetStream, string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("O arquivo enviado deve estar no formato .xlsx.");
        }

        var spreadsheetBuffer = new MemoryStream();
        spreadsheetStream.CopyTo(spreadsheetBuffer);
        spreadsheetBuffer.Position = 0;
        return EnumerateRows(spreadsheetBuffer);
    }

    private static IEnumerable<SpreadsheetRow> EnumerateRows(MemoryStream spreadsheetBuffer)
    {
        using var spreadsheetBufferScope = spreadsheetBuffer;
        ZipArchive spreadsheetArchive;
        try
        {
            spreadsheetArchive = new ZipArchive(spreadsheetBuffer, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException exception)
        {
            throw new ValidationException(
                $"O arquivo informado não é uma planilha XLSX válida: {exception.Message}");
        }

        using var spreadsheetArchiveScope = spreadsheetArchive;
        IReadOnlyList<string> sharedStrings;
        ZipArchiveEntry worksheetEntry;
        try
        {
            sharedStrings = ReadSharedStrings(spreadsheetArchive);
            worksheetEntry = FindFirstWorksheet(spreadsheetArchive);
        }
        catch (XmlException exception)
        {
            throw new ValidationException(
                $"A estrutura XML da planilha é inválida: {exception.Message}");
        }

        using var worksheetStream = worksheetEntry.Open();
        using var worksheetReader = XmlReader.Create(
            worksheetStream,
            new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });

        while (ReadToFollowingRow(worksheetReader))
        {
            XElement rowElement;
            using (var rowReader = worksheetReader.ReadSubtree())
            {
                rowElement = XElement.Load(rowReader);
            }

            yield return new SpreadsheetRow(
                ReadRowNumber(rowElement),
                ReadCellValues(rowElement, sharedStrings));
        }
    }

    /// <summary>
    /// Um XmlException lançado durante a varredura precisa virar erro de
    /// validação, mas blocos try/catch não podem envolver um `yield return`.
    /// </summary>
    private static bool ReadToFollowingRow(XmlReader worksheetReader)
    {
        try
        {
            return worksheetReader.ReadToFollowing("row", SpreadsheetNamespace.NamespaceName);
        }
        catch (XmlException exception)
        {
            throw new ValidationException(
                $"A estrutura XML da planilha é inválida: {exception.Message}");
        }
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive spreadsheetArchive)
    {
        var sharedStringsEntry = spreadsheetArchive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        using var sharedStringsStream = sharedStringsEntry.Open();
        var sharedStringsDocument = XDocument.Load(sharedStringsStream);
        return sharedStringsDocument
            .Descendants(SpreadsheetNamespace + "si")
            .Select(sharedString => string.Concat(
                sharedString
                    .Descendants(SpreadsheetNamespace + "t")
                    .Select(text => text.Value)))
            .ToArray();
    }

    private static ZipArchiveEntry FindFirstWorksheet(ZipArchive spreadsheetArchive)
    {
        var workbookEntry = spreadsheetArchive.GetEntry("xl/workbook.xml")
            ?? throw new ValidationException("A planilha não possui o arquivo workbook.xml.");
        var relationshipsEntry = spreadsheetArchive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new ValidationException("A planilha não possui os relacionamentos do workbook.");

        using var workbookStream = workbookEntry.Open();
        using var relationshipsStream = relationshipsEntry.Open();
        var workbookDocument = XDocument.Load(workbookStream);
        var relationshipsDocument = XDocument.Load(relationshipsStream);
        var firstSheet = workbookDocument
            .Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault()
            ?? throw new ValidationException("A planilha não possui nenhuma aba.");
        var relationshipId = firstSheet.Attribute(OfficeRelationshipNamespace + "id")?.Value
            ?? throw new ValidationException("A primeira aba não possui um relacionamento válido.");
        var worksheetTarget = relationshipsDocument
            .Descendants(PackageRelationshipNamespace + "Relationship")
            .SingleOrDefault(relationship => relationship.Attribute("Id")?.Value == relationshipId)
            ?.Attribute("Target")
            ?.Value
            ?? throw new ValidationException("Não foi possível localizar a primeira aba da planilha.");
        var normalizedWorksheetPath = worksheetTarget.StartsWith("/", StringComparison.Ordinal)
            ? worksheetTarget.TrimStart('/')
            : $"xl/{worksheetTarget.TrimStart('/')}";

        return spreadsheetArchive.GetEntry(normalizedWorksheetPath)
            ?? throw new ValidationException("O arquivo da primeira aba não foi encontrado.");
    }

    private static IReadOnlyDictionary<string, string> ReadCellValues(
        XElement rowElement,
        IReadOnlyList<string> sharedStrings)
    {
        var cellValues = new Dictionary<string, string>();
        foreach (var cellElement in rowElement.Elements(SpreadsheetNamespace + "c"))
        {
            var cellReference = cellElement.Attribute("r")?.Value;
            var columnName = cellReference is null
                ? null
                : CellColumnRegex().Match(cellReference).Value;
            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            var cellType = cellElement.Attribute("t")?.Value;
            var rawValue = cellElement.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
            string cellValue;
            if (cellType == "s"
                && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
                && sharedStringIndex >= 0
                && sharedStringIndex < sharedStrings.Count)
            {
                cellValue = sharedStrings[sharedStringIndex];
            }
            else if (cellType == "inlineStr")
            {
                cellValue = string.Concat(
                    cellElement
                        .Descendants(SpreadsheetNamespace + "t")
                        .Select(text => text.Value));
            }
            else
            {
                cellValue = rawValue;
            }

            cellValues[columnName] = cellValue.Trim();
        }

        return cellValues;
    }

    private static int ReadRowNumber(XElement rowElement)
    {
        return int.TryParse(
            rowElement.Attribute("r")?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var rowNumber)
            ? rowNumber
            : 0;
    }

    [GeneratedRegex("^[A-Z]+", RegexOptions.CultureInvariant)]
    private static partial Regex CellColumnRegex();
}

/// <summary>Uma linha da planilha, com os valores indexados pela letra da coluna.</summary>
public sealed record SpreadsheetRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> CellValuesByColumn)
{
    public string ReadColumn(string? columnName)
    {
        return columnName is not null && CellValuesByColumn.TryGetValue(columnName, out var value)
            ? value
            : string.Empty;
    }
}

/// <summary>
/// Comparação de textos vindos da planilha sem acento e sem diferenciar
/// maiúsculas, usada tanto para reconhecer cabeçalhos quanto para deduplicar.
/// </summary>
public static class SpreadsheetText
{
    public static string Normalize(string value)
    {
        var normalizedValue = value.Trim().Normalize(NormalizationForm.FormD);
        var characters = normalizedValue
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(characters).Normalize(NormalizationForm.FormC);
    }
}
