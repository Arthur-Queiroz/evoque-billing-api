using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

/// <summary>
/// Validação contra a exportação real do CRM 2.0 do EVO usada em julho/2026.
///
/// A planilha fica fora do repositório porque contém dados de pessoas reais.
/// Quando o arquivo não está presente — em CI, por exemplo — o teste não tem o
/// que verificar e termina sem afirmar nada.
/// </summary>
public sealed class RealEvoExportValidationTests
{
    private const string RealExportPath = @"C:\prog\evoque\docs\file_export_evo_manual.xlsx";

    [Fact]
    public void Read_DiscoversEveryCompanyOfTheRealExportRegardlessOfContractAmount()
    {
        if (!File.Exists(RealExportPath))
        {
            return;
        }

        using var spreadsheetStream = File.OpenRead(RealExportPath);
        var reader = new CompanyCatalogSpreadsheetReader(new SpreadsheetWorkbookReader());

        var catalog = reader.Read(spreadsheetStream, Path.GetFileName(RealExportPath));

        // 572 linhas analisadas = 512 pessoas com empresa reconhecida + 60 sem
        // CNPJ no padrão esperado. As 63 empresas incluem as 39 que aparecem em
        // linhas com valor positivo mais 24 que só existem em linhas com valor
        // vazio, zero ou inválido — exatamente o que o catálogo precisa achar.
        Assert.Equal(572, catalog.AnalyzedRowCount);
        Assert.Equal(63, catalog.Companies.Count);
        Assert.Equal(512, catalog.Companies.Sum(company => company.Members.Count));
        Assert.Equal(60, catalog.Warnings.Count);
        Assert.All(catalog.Warnings, warning => Assert.Equal("MissingCompanyTaxId", warning.Code));

        // As duas empresas conferidas manualmente contra as planilhas de
        // fechamento validadas.
        AssertCompany(catalog, "56087276000103", "OPEN SPORTS LTDA", 24);
        AssertCompany(catalog, "43322169000170", "WEB PRADO CONSULTORIA EM MARKETING E TREINAMENTO LTDA", 4);
    }

    private static void AssertCompany(
        ImportedCompanyCatalog catalog,
        string taxId,
        string expectedEvoName,
        int expectedMemberCount)
    {
        var company = Assert.Single(catalog.Companies, candidate => candidate.TaxId == taxId);
        Assert.Equal(expectedEvoName, company.EvoName);
        Assert.Equal(expectedMemberCount, company.Members.Count);
    }
}
