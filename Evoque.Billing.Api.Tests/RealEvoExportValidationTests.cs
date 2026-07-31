using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Http;

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

    private const string UnionTaxId = "57571077000139";
    private const string OpenSportsTaxId = "56087276000103";

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
        // CNPJ no padrão esperado. Os 63 CNPJs distintos são o que o leitor
        // encontra na coluna Profissão; a maioria é o empregador da pessoa, não
        // um cliente corporativo. Descobrir não é cadastrar: quem decide se a
        // empresa existe é o catálogo, conferido em
        // SynchronizeAsync_CreatesNoCompanyFromTheProductionAttemptSpreadsheet.
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

    /// <summary>
    /// Regressão da falha de produção: esta é a exportação que cadastrou
    /// sindicatos, igrejas, GM, SEBRAE e os planos "AMIGOS EVOQUE" como clientes.
    /// Agora ela não pode criar nenhuma empresa — só vincula pessoas a quem já
    /// está no catálogo.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_CreatesNoCompanyFromTheRealExport()
    {
        if (!File.Exists(RealExportPath))
        {
            return;
        }

        var dataStore = new InMemoryBillingDataStore();
        var importService = CreateImportService(dataStore, out _, out _);

        var result = await importService.SynchronizeAsync(
            OpenRealExport(),
            "operador@evoque",
            CancellationToken.None);

        Assert.Empty(dataStore.Companies);
        Assert.Equal(0, result.LinkedCompanyCount);

        // Os 63 CNPJs achados na coluna Profissão viram pendência, não cadastro.
        Assert.Equal(63, result.UnregisteredCompanies.Count);
        Assert.Contains(result.UnregisteredCompanies, company => company.TaxId == UnionTaxId);

        // Nenhuma pessoa é vinculada enquanto não houver empresa cadastrada.
        Assert.Empty(dataStore.CorporateMembers);
        Assert.Equal(0, result.MemberComparison.NewMemberCount);
        Assert.Equal(0, result.MemberComparison.DepartedMemberCount);
        Assert.Equal(512, result.MemberComparison.UnregisteredCompanyMemberCount);
    }

    [Fact]
    public async Task SynchronizeAsync_LinksOnlyTheCompanyRegisteredInTheCatalog()
    {
        if (!File.Exists(RealExportPath))
        {
            return;
        }

        var dataStore = new InMemoryBillingDataStore();
        var importService = CreateImportService(
            dataStore,
            out var companyRepository,
            out var corporateMemberRepository);
        await companyRepository.UpsertAsync(
            Domain.Company.CreateManually(
                OpenSportsTaxId,
                "Open Sports",
                "operador@evoque",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var result = await importService.SynchronizeAsync(
            OpenRealExport(),
            "operador@evoque",
            CancellationToken.None);

        Assert.Equal(1, result.LinkedCompanyCount);
        Assert.Equal(62, result.UnregisteredCompanies.Count);
        Assert.Equal(24, result.MemberComparison.NewMemberCount);
        Assert.All(
            await corporateMemberRepository.ListAsync(CancellationToken.None),
            member => Assert.Equal(OpenSportsTaxId, member.CompanyTaxId));
    }

    private static CompanyCatalogImportService CreateImportService(
        InMemoryBillingDataStore dataStore,
        out InMemoryCompanyRepository companyRepository,
        out InMemoryCorporateMemberRepository corporateMemberRepository)
    {
        companyRepository = new InMemoryCompanyRepository(dataStore);
        corporateMemberRepository = new InMemoryCorporateMemberRepository(dataStore);
        return new CompanyCatalogImportService(
            new CompanyCatalogSpreadsheetReader(new SpreadsheetWorkbookReader()),
            companyRepository,
            new InMemoryCompanyCatalogImportRepository(dataStore),
            new CorporateMemberService(corporateMemberRepository, companyRepository));
    }

    private static FormFile OpenRealExport()
    {
        var spreadsheetContent = new MemoryStream(File.ReadAllBytes(RealExportPath));
        return new FormFile(
            spreadsheetContent,
            0,
            spreadsheetContent.Length,
            "file",
            Path.GetFileName(RealExportPath));
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
