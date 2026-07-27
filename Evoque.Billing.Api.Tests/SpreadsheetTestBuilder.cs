using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Evoque.Billing.Api.Tests;

/// <summary>
/// Monta um XLSX mínimo em memória para os testes, com as colunas informadas no
/// cabeçalho e as linhas na ordem recebida.
/// </summary>
public static class SpreadsheetTestBuilder
{
    public static MemoryStream Create(IReadOnlyList<string> headers, params string[][] rows)
    {
        var spreadsheetStream = new MemoryStream();
        using (var archive = new ZipArchive(spreadsheetStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Dados" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(headers, rows));
        }

        spreadsheetStream.Position = 0;
        return spreadsheetStream;
    }

    public static IFormFile CreateFormFile(
        string fileName,
        IReadOnlyList<string> headers,
        params string[][] rows)
    {
        var spreadsheetStream = Create(headers, rows);
        return new FormFile(spreadsheetStream, 0, spreadsheetStream.Length, "file", fileName);
    }

    private static string BuildWorksheet(IReadOnlyList<string> headers, string[][] rows)
    {
        var worksheet = new StringBuilder();
        worksheet.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        worksheet.AppendLine("<sheetData>");
        worksheet.AppendLine(BuildRow(1, headers));
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            worksheet.AppendLine(BuildRow(rowIndex + 2, rows[rowIndex]));
        }

        worksheet.AppendLine("</sheetData>");
        worksheet.AppendLine("</worksheet>");
        return worksheet.ToString();
    }

    private static string BuildRow(int rowNumber, IReadOnlyList<string> cellValues)
    {
        var row = new StringBuilder($"""<row r="{rowNumber}">""");
        for (var columnIndex = 0; columnIndex < cellValues.Count; columnIndex++)
        {
            var columnName = (char)('A' + columnIndex);
            var escapedValue = System.Security.SecurityElement.Escape(cellValues[columnIndex]) ?? string.Empty;
            row.Append(
                $"""<c r="{columnName}{rowNumber}" t="inlineStr"><is><t>{escapedValue}</t></is></c>""");
        }

        row.Append("</row>");
        return row.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
