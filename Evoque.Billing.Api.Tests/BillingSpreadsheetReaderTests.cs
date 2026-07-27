using System.IO.Compression;
using System.Text;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class BillingSpreadsheetReaderTests
{
    [Fact]
    public void Read_ImportsConsecutiveRowsAndGroupsCompanyData()
    {
        using var spreadsheetStream = CreateSpreadsheet();
        var reader = new BillingSpreadsheetReader(new SpreadsheetWorkbookReader());

        var importedSpreadsheet = reader.Read(spreadsheetStream, "fechamento.xlsx");

        Assert.Equal(2, importedSpreadsheet.Rows.Count);
        Assert.Equal(199.80m, importedSpreadsheet.Rows.Sum(row => row.Amount));
        Assert.All(importedSpreadsheet.Rows, row => Assert.Equal("12345678000199", row.CompanyTaxId));
    }

    private static MemoryStream CreateSpreadsheet()
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
                  <sheets><sheet name="Fechamento" sheetId="1" r:id="rId1"/></sheets>
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
            WriteEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" t="inlineStr"><is><t>Nome</t></is></c>
                      <c r="B1" t="inlineStr"><is><t>Contrato</t></is></c>
                      <c r="C1" t="inlineStr"><is><t>Empresa</t></is></c>
                      <c r="D1" t="inlineStr"><is><t>Valor do contrato</t></is></c>
                    </row>
                    <row r="2">
                      <c r="A2" t="inlineStr"><is><t>Pessoa Um</t></is></c>
                      <c r="B2" t="inlineStr"><is><t>Plano corporativo</t></is></c>
                      <c r="C2" t="inlineStr"><is><t>Empresa Teste - 12345678000199</t></is></c>
                      <c r="D2"><v>99.9</v></c>
                    </row>
                    <row r="3">
                      <c r="A3" t="inlineStr"><is><t>Pessoa Dois</t></is></c>
                      <c r="B3" t="inlineStr"><is><t>Plano corporativo</t></is></c>
                      <c r="C3" t="inlineStr"><is><t>Empresa Teste - 12345678000199</t></is></c>
                      <c r="D3"><v>99.9</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        spreadsheetStream.Position = 0;
        return spreadsheetStream;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
