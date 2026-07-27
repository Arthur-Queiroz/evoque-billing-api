using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class CompanyCatalogSpreadsheetReaderTests
{
    private const string OpenSportsTaxId = "56087276000103";
    private const string WebPradoTaxId = "43322169000170";
    private static readonly string[] EvoExportHeaders = ["Nome", "Contrato", "Profissão", "Valor do contrato"];

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("valor indisponível")]
    public void Read_DiscoversCompanyEvenWhenContractAmountIsEmptyZeroOrInvalid(string amountValue)
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano corporativo", $"Open Sports - {OpenSportsTaxId}", amountValue]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        var company = Assert.Single(catalog.Companies);
        Assert.Equal(OpenSportsTaxId, company.TaxId);
        Assert.Equal("Open Sports", company.EvoName);
        Assert.Single(company.Members);
    }

    [Fact]
    public void Read_GroupsEveryMemberOfTheSameTaxIdIntoOneCompany()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", "99,90"],
            ["Pessoa Dois", "Plano", $"Open Sports - {OpenSportsTaxId}", ""],
            ["Pessoa Tres", "Plano", $"Open Sports - {OpenSportsTaxId}", "0"]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        var company = Assert.Single(catalog.Companies);
        Assert.Equal(3, company.Members.Count);
    }

    [Fact]
    public void Read_DeduplicatesRepeatedMembersOfTheSameCompany()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", "99,90"],
            ["pessoa um", "Plano", $"Open Sports - {OpenSportsTaxId}", "99,90"],
            ["PESSOA UM", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        var company = Assert.Single(catalog.Companies);
        Assert.Single(company.Members);
        Assert.Equal(2, catalog.DuplicateMemberCount);
    }

    [Fact]
    public void Read_KeepsOneCompanyAndWarnsWhenTheSameTaxIdHasDifferentNames()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""],
            ["Pessoa Dois", "Plano", $"Open Sports - {OpenSportsTaxId}", ""],
            ["Pessoa Tres", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        var company = Assert.Single(catalog.Companies);
        Assert.Equal("Open Sports", company.EvoName);
        var warning = Assert.Single(catalog.Warnings);
        Assert.Equal("CompanyNameConflict", warning.Code);
    }

    [Fact]
    public void Read_WarnsWithRowNumberWhenTheTaxIdCheckDigitsAreInvalid()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", "Empresa Falsa - 12345678000199", "99,90"]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        Assert.Empty(catalog.Companies);
        var warning = Assert.Single(catalog.Warnings);
        Assert.Equal("InvalidTaxId", warning.Code);
        Assert.Equal(2, warning.SourceRowNumber);
    }

    [Fact]
    public void Read_WarnsWithoutInventingCompaniesWhenTheRowHasNoTaxId()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", "Personal Trainer", "99,90"],
            ["Pessoa Dois", "Plano", $"Web Prado - {WebPradoTaxId}", ""]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        var company = Assert.Single(catalog.Companies);
        Assert.Equal(WebPradoTaxId, company.TaxId);
        var warning = Assert.Single(catalog.Warnings);
        Assert.Equal("MissingCompanyTaxId", warning.Code);
    }

    [Fact]
    public void Read_AcceptsTaxIdWithFormattingInTheCompanyColumn()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            EvoExportHeaders,
            ["Pessoa Um", "Plano", "Web Prado - 43.322.169/0001-70", ""]);

        var catalog = CreateReader().Read(spreadsheet, "export.xlsx");

        Assert.Equal(WebPradoTaxId, Assert.Single(catalog.Companies).TaxId);
    }

    [Fact]
    public void Read_RejectsFilesThatAreNotXlsx()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(EvoExportHeaders);

        Assert.Throws<ValidationException>(() => CreateReader().Read(spreadsheet, "export.csv"));
    }

    [Fact]
    public void Read_ThrowsWhenTheCompanyColumnIsMissing()
    {
        using var spreadsheet = SpreadsheetTestBuilder.Create(
            ["Nome", "Contrato"],
            ["Pessoa Um", "Plano"]);

        Assert.Throws<ValidationException>(() => CreateReader().Read(spreadsheet, "export.xlsx"));
    }

    private static CompanyCatalogSpreadsheetReader CreateReader()
    {
        return new CompanyCatalogSpreadsheetReader(new SpreadsheetWorkbookReader());
    }
}
