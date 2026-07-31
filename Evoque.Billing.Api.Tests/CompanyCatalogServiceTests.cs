using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.CompanyRegistry;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Tests;

public sealed class CompanyCatalogServiceTests
{
    private const string OpenSportsTaxId = "56087276000103";
    private const string WebPradoTaxId = "43322169000170";

    /// <summary>
    /// CNPJ real do sindicato dos metalúrgicos, que apareceu na exportação do
    /// EVO com 47 pessoas e foi cadastrado como empresa pela lógica antiga.
    /// </summary>
    private const string UnionTaxId = "57571077000139";

    private const string OperatorId = "operador@evoque";
    private static readonly string[] EvoExportHeaders =
        ["IdCliente", "Nome", "IdContrato", "Contrato", "Profissão", "Valor do contrato"];

    [Fact]
    public async Task SynchronizeAsync_NeverCreatesACompanyForATaxIdOutsideTheCatalog()
    {
        // A coluna Profissão traz o empregador da pessoa. Um sindicato aparece
        // com CNPJ válido e dezenas de pessoas, mas não é cliente corporativo.
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20, OperatorId),
            CancellationToken.None);

        var result = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(
                ["Pessoa Um", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""],
                ["Pessoa Dois", "Plano", $"SINDICATO DOS METALURGICOS - {UnionTaxId}", ""],
                ["Pessoa Tres", "Plano", $"SINDICATO DOS METALURGICOS - {UnionTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, result.LinkedCompanyCount);
        var unregistered = Assert.Single(result.UnregisteredCompanies);
        Assert.Equal(UnionTaxId, unregistered.TaxId);
        Assert.Equal(2, unregistered.MemberCount);

        var companies = await catalog.Service.ListAsync(new ListCompaniesQuery(), CancellationToken.None);
        Assert.Equal(OpenSportsTaxId, Assert.Single(companies).TaxId);
    }

    [Fact]
    public async Task SynchronizeAsync_LinksMembersOnlyToRegisteredCompanies()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20, OperatorId),
            CancellationToken.None);

        var result = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(
                ["Pessoa Um", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""],
                ["Pessoa Dois", "Plano", $"SINDICATO DOS METALURGICOS - {UnionTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, result.MemberComparison.NewMemberCount);
        Assert.Equal(1, result.MemberComparison.UnregisteredCompanyMemberCount);
        var members = await catalog.Service.ListMembersAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal("Pessoa Um", Assert.Single(members).MemberName);
    }

    [Fact]
    public async Task SynchronizeAsync_DoesNotDeactivateAMemberWhoseCompanyIsNotRegistered()
    {
        // Uma lacuna do catálogo não pode inativar pessoa silenciosamente: ela
        // fica intocada e o CNPJ desconhecido vira pendência para revisão.
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20, OperatorId),
            CancellationToken.None);
        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(
                ["Pessoa Um", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""],
                ["Pessoa Dois", "Plano", $"SINDICATO DOS METALURGICOS - {UnionTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var members = await catalog.Service.ListMembersAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.True(Assert.Single(members).IsActive);
    }

    [Fact]
    public async Task SynchronizeAsync_IgnoresAnExistingCompanyWithoutChangingAnyData()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.UpdateAsync(
            OpenSportsTaxId,
            new UpdateCompanyRequest("Open Sports Matriz", 20, OperatorId),
            CancellationToken.None);
        LinkAsaasCustomer(catalog, OpenSportsTaxId, AsaasEnvironment.Sandbox, "cus_sandbox");
        LinkAsaasCustomer(catalog, OpenSportsTaxId, AsaasEnvironment.Production, "cus_producao");

        var result = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(
                ["Pessoa Um", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""],
                ["Pessoa Dois", "Plano", $"OPEN SPORTS LTDA - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var company = await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal(1, result.LinkedCompanyCount);
        Assert.Empty(result.UnregisteredCompanies);
        Assert.Equal("Open Sports Matriz", company.DisplayName);
        Assert.Equal(20, company.ClosingDay);
        Assert.True(company.HasActiveSchedule);
        Assert.Equal("cus_sandbox", company.AsaasSandboxCustomerId);
        Assert.Equal("cus_producao", company.AsaasProductionCustomerId);
        Assert.Equal(2, company.MemberCount);
    }

    [Fact]
    public async Task SynchronizeAsync_KeepsAnInactiveCompanyInactiveWhenItReappears()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.DeactivateAsync(
            OpenSportsTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);

        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var company = await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.False(company.IsActive);
        Assert.False(company.RequiresReviewAfterReappearing);
    }

    [Fact]
    public async Task SynchronizeAsync_DoesNotDeactivateCompaniesMissingFromTheSpreadsheet()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(WebPradoTaxId, "Web Prado", null, OperatorId),
            CancellationToken.None);

        var result = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, result.LinkedCompanyCount);
        Assert.Empty(result.UnregisteredCompanies);
        var webPrado = await catalog.Service.GetAsync(WebPradoTaxId, CancellationToken.None);
        Assert.True(webPrado.IsActive);
    }

    [Fact]
    public async Task SynchronizeAsync_KeepsManuallyRegisteredCompaniesAbsentFromTheSpreadsheet()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(WebPradoTaxId, "Web Prado", 20, OperatorId),
            CancellationToken.None);

        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var webPrado = await catalog.Service.GetAsync(WebPradoTaxId, CancellationToken.None);
        Assert.Equal("Manual", webPrado.Source);
        Assert.True(webPrado.IsActive);
        Assert.Equal(20, webPrado.ClosingDay);
    }

    [Theory]
    [InlineData(CompanyRegistryLookupStatus.NotFound)]
    [InlineData(CompanyRegistryLookupStatus.Unavailable)]
    public async Task CreateAsync_CompletesWhenTheRegistryFailsOrReturnsNotFound(
        CompanyRegistryLookupStatus lookupStatus)
    {
        var catalog = CreateCatalog(new StubCompanyRegistryGateway(lookupStatus));

        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);

        var company = await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal(lookupStatus.ToString(), company.RegistryLookupStatus);
        Assert.Equal("Open Sports", company.DisplayName);
    }

    [Fact]
    public async Task CreateAsync_EnrichesTheCompanyWithoutOverwritingTheOperationalName()
    {
        var catalog = CreateCatalog();

        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);

        var company = await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal("Open Sports", company.DisplayName);
        Assert.Equal("OPEN SPORTS LTDA", company.LegalName);
        Assert.Equal("ATIVA", company.RegistrationStatus);
        Assert.Equal("Found", company.RegistryLookupStatus);
        Assert.Equal("GOIANIA", company.RegistryAddress?.City);
    }

    [Fact]
    public async Task SynchronizeAsync_RecordsTheMemberSnapshotOfTheLatestImport()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(
                ["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""],
                ["Pessoa Dois", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var members = await catalog.Service.ListMembersAsync(OpenSportsTaxId, CancellationToken.None);

        Assert.Equal(
            ["Pessoa Dois", "Pessoa Um"],
            members.Select(member => member.MemberName).ToArray());
    }

    [Fact]
    public async Task CreateAsync_RejectsAnInvalidTaxIdAndADuplicatedCompany()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => catalog.Service.CreateAsync(
            new CreateCompanyRequest("12345678000199", "Empresa Falsa", null, OperatorId),
            CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UsesRegistryNameWhenTheOperatorProvidesOnlyTheTaxId()
    {
        var catalog = CreateCatalog();

        var company = await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, null, 20, OperatorId),
            CancellationToken.None);

        Assert.Equal("OPEN SPORTS", company.DisplayName);
        Assert.Equal("OPEN SPORTS LTDA", company.LegalName);
        Assert.Equal("Found", company.RegistryLookupStatus);
        Assert.Equal(20, company.ClosingDay);
    }

    [Fact]
    public async Task CreateAsync_InvalidClosingDayDoesNotPersistTheCompany()
    {
        var catalog = CreateCatalog();

        await Assert.ThrowsAsync<ValidationException>(() => catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 3, OperatorId),
            CancellationToken.None));

        Assert.Empty(catalog.DataStore.Companies);
        Assert.Empty(catalog.DataStore.CompanyBillingSchedules);
    }

    [Fact]
    public async Task UpdateAsync_InvalidClosingDayDoesNotPersistAnyChange()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20, OperatorId),
            CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => catalog.Service.UpdateAsync(
            OpenSportsTaxId,
            new UpdateCompanyRequest("Nome que não deve ser salvo", 3, OperatorId),
            CancellationToken.None));

        var company = await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal("Open Sports", company.DisplayName);
        Assert.Equal(20, company.ClosingDay);
        Assert.Null(company.AsaasSandboxCustomerId);
    }

    [Fact]
    public async Task SynchronizeAsync_CompletesWhenTheRegistryGatewayThrowsUnexpectedly()
    {
        var catalog = CreateCatalog(new ThrowingCompanyRegistryGateway());
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);

        var result = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, result.LinkedCompanyCount);
        Assert.Single(catalog.DataStore.Companies);
        Assert.Single(catalog.DataStore.CompanyCatalogImports);
    }

    [Fact]
    public async Task DeactivateAndReactivate_ChangeTheStatusWithoutDeletingTheCompany()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20, OperatorId),
            CancellationToken.None);

        var deactivated = await catalog.Service.DeactivateAsync(
            OpenSportsTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);
        Assert.False(deactivated.IsActive);
        Assert.False(deactivated.HasActiveSchedule);

        var reactivated = await catalog.Service.ReactivateAsync(
            OpenSportsTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);
        Assert.True(reactivated.IsActive);
        Assert.NotNull(await catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ThrowsNotFoundForACompanyOutsideTheCatalog()
    {
        var catalog = CreateCatalog();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            catalog.Service.GetAsync(OpenSportsTaxId, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_AppliesEveryDocumentedFilter()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(WebPradoTaxId, "Web Prado", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.UpdateAsync(
            OpenSportsTaxId,
            new UpdateCompanyRequest("Open Sports", 20, OperatorId),
            CancellationToken.None);
        LinkAsaasCustomer(catalog, OpenSportsTaxId, AsaasEnvironment.Sandbox, "cus_sandbox");
        await catalog.Service.DeactivateAsync(
            WebPradoTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);

        Assert.Equal(
            OpenSportsTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(Status: "active"))).TaxId);
        Assert.Equal(
            WebPradoTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(Status: "inactive"))).TaxId);
        Assert.Equal(
            OpenSportsTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(ClosingDay: 20))).TaxId);
        Assert.Equal(
            WebPradoTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(WithoutClosingDay: true))).TaxId);
        Assert.Equal(
            OpenSportsTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(AsaasLink: "configured"))).TaxId);
        Assert.Equal(
            WebPradoTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(AsaasLink: "pending"))).TaxId);
        Assert.Equal(2, (await ListAsync(catalog, new ListCompaniesQuery(Source: "Manual"))).Count);
        Assert.Empty(await ListAsync(catalog, new ListCompaniesQuery(Source: "EvoSpreadsheet")));
        Assert.Equal(
            OpenSportsTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(Search: "open"))).TaxId);
        Assert.Equal(
            WebPradoTaxId,
            Assert.Single(await ListAsync(catalog, new ListCompaniesQuery(Search: "43.322.169"))).TaxId);
    }

    [Fact]
    public async Task ListBillingHistoryAsync_ReturnsOnlyTheDraftsOfTheRequestedTaxId()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(WebPradoTaxId, "Web Prado", null, OperatorId),
            CancellationToken.None);
        await AddBillingDraftAsync(catalog, OpenSportsTaxId, "Open Sports", 120m);
        await AddBillingDraftAsync(catalog, WebPradoTaxId, "Web Prado", 439.60m);

        var history = await catalog.Service.ListBillingHistoryAsync(WebPradoTaxId, CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Equal(439.60m, entry.TotalAmount);
    }

    [Fact]
    public async Task CatalogOperations_NeverCreateAnAsaasCharge()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);

        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExport(["Pessoa Um", "Plano", $"Open Sports - {OpenSportsTaxId}", "99,90"]),
            OperatorId,
            CancellationToken.None);
        await catalog.Service.UpdateAsync(
            OpenSportsTaxId,
            new UpdateCompanyRequest("Open Sports", 20, OperatorId),
            CancellationToken.None);
        LinkAsaasCustomer(catalog, OpenSportsTaxId, AsaasEnvironment.Sandbox, "cus_sandbox");
        LinkAsaasCustomer(catalog, OpenSportsTaxId, AsaasEnvironment.Production, "cus_producao");
        await catalog.Service.RefreshRegistryAsync(
            OpenSportsTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);

        // O catálogo não cria prévia de faturamento, então também não existe
        // nada que pudesse virar cobrança no Asaas.
        Assert.Empty(catalog.DataStore.BillingDrafts);
        Assert.Empty(catalog.DataStore.ChargeBatches);
    }

    private static async Task<IReadOnlyCollection<CompanyResponse>> ListAsync(
        TestCatalog catalog,
        ListCompaniesQuery query)
    {
        return await catalog.Service.ListAsync(query, CancellationToken.None);
    }

    private static async Task AddBillingDraftAsync(
        TestCatalog catalog,
        string taxId,
        string companyName,
        decimal amount)
    {
        var billingDraft = new BillingDraft(
            Guid.NewGuid(),
            taxId,
            companyName,
            taxId,
            null,
            [new BillingDraftItem("Plano corporativo", 1, amount, null)],
            DateTimeOffset.UtcNow);
        await new InMemoryBillingDraftRepository(catalog.DataStore).AddAsync(
            billingDraft,
            CancellationToken.None);
    }

    private static void LinkAsaasCustomer(
        TestCatalog catalog,
        string taxId,
        AsaasEnvironment asaasEnvironment,
        string customerId)
    {
        var company = catalog.DataStore.Companies[taxId];
        company.LinkAsaasCustomer(
            asaasEnvironment,
            customerId,
            OperatorId,
            DateTimeOffset.UtcNow);
    }

    private static IFormFile CreateEvoExport(params string[][] rows)
    {
        var memberIdsByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var contractIdsByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var expandedRows = rows.Select(row =>
        {
            var memberName = row[0];
            var contractName = row[1];
            if (!memberIdsByName.TryGetValue(memberName, out var memberId))
            {
                memberId = 10000 + memberIdsByName.Count;
                memberIdsByName.Add(memberName, memberId);
            }

            if (!contractIdsByName.TryGetValue(contractName, out var contractId))
            {
                contractId = 20000 + contractIdsByName.Count;
                contractIdsByName.Add(contractName, contractId);
            }

            return new[]
            {
                memberId.ToString(),
                memberName,
                contractId.ToString(),
                contractName,
                row[2],
                row[3],
            };
        }).ToArray();
        return SpreadsheetTestBuilder.CreateFormFile(
            "file_export_evo_manual.xlsx",
            EvoExportHeaders,
            expandedRows);
    }

    [Fact]
    public async Task SynchronizeAsync_UpdatesPersistentCorporateMemberSnapshot()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExportWithIds(
                ["30001", "Pessoa Um", "40001", "Plano A", $"Open Sports - {OpenSportsTaxId}", ""],
                ["30002", "Pessoa Dois", "40002", "Plano B", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var secondImport = await catalog.ImportService.SynchronizeAsync(
            CreateEvoExportWithIds(
                ["30001", "Pessoa Um Atualizada", "40003", "Plano C", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, secondImport.MemberComparison.RetainedMemberCount);
        Assert.Equal(1, secondImport.MemberComparison.DepartedMemberCount);
        var members = await catalog.Service.ListMembersAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal(2, members.Count);
        Assert.Contains(members, member =>
            member.EvoMemberId == 30001
            && member.IsActive
            && member.MemberName == "Pessoa Um Atualizada"
            && member.Contracts.SequenceEqual(["Plano C"]));
        Assert.Contains(members, member => member.EvoMemberId == 30002 && !member.IsActive);
    }

    [Fact]
    public async Task SynchronizeAsync_BlocksCompanyConflictForTheSameEvoMemberId()
    {
        var catalog = CreateCatalog();
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", null, OperatorId),
            CancellationToken.None);
        await catalog.Service.CreateAsync(
            new CreateCompanyRequest(WebPradoTaxId, "Web Prado", null, OperatorId),
            CancellationToken.None);
        await catalog.ImportService.SynchronizeAsync(
            CreateEvoExportWithIds(
                ["30001", "Pessoa Um", "40001", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            CancellationToken.None);

        var conflictingFile = CreateEvoExportWithIds(
            ["30001", "Pessoa Um", "40001", "Plano", $"Web Prado - {WebPradoTaxId}", ""]);
        var preview = await catalog.ImportService.PreviewAsync(
            conflictingFile,
            CancellationToken.None);
        Assert.Equal(1, preview.MemberComparison.ConflictMemberCount);

        conflictingFile = CreateEvoExportWithIds(
            ["30001", "Pessoa Um", "40001", "Plano", $"Web Prado - {WebPradoTaxId}", ""]);
        await Assert.ThrowsAsync<ConflictException>(() => catalog.ImportService.SynchronizeAsync(
            conflictingFile,
            OperatorId,
            completeSnapshotConfirmed: true,
            CancellationToken.None));
        // O vínculo original permanece: o conflito bloqueia a importação inteira
        // em vez de mover a pessoa de empresa.
        Assert.Equal(
            30001,
            Assert.Single(await catalog.Service.ListMembersAsync(OpenSportsTaxId, CancellationToken.None))
                .EvoMemberId);
        Assert.Empty(await catalog.Service.ListMembersAsync(WebPradoTaxId, CancellationToken.None));
    }

    [Fact]
    public async Task SynchronizeAsync_RequiresCompleteSnapshotConfirmationBeforeChangingData()
    {
        var catalog = CreateCatalog();

        await Assert.ThrowsAsync<ValidationException>(() => catalog.ImportService.SynchronizeAsync(
            CreateEvoExportWithIds(
                ["30001", "Pessoa Um", "40001", "Plano", $"Open Sports - {OpenSportsTaxId}", ""]),
            OperatorId,
            completeSnapshotConfirmed: false,
            CancellationToken.None));

        Assert.Empty(await catalog.Service.ListAsync(
            new ListCompaniesQuery(),
            CancellationToken.None));
    }

    private static IFormFile CreateEvoExportWithIds(params string[][] rows)
    {
        return SpreadsheetTestBuilder.CreateFormFile(
            "file_export_evo_manual.xlsx",
            EvoExportHeaders,
            rows);
    }

    private static TestCatalog CreateCatalog(ICompanyRegistryGateway? companyRegistryGateway = null)
    {
        var dataStore = new InMemoryBillingDataStore();
        var companyRepository = new InMemoryCompanyRepository(dataStore);
        var companyCatalogImportRepository = new InMemoryCompanyCatalogImportRepository(dataStore);
        var corporateMemberRepository = new InMemoryCorporateMemberRepository(dataStore);
        var companyBillingScheduleRepository = new InMemoryCompanyBillingScheduleRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var auditLogRepository = new InMemoryAuditLogRepository(dataStore);
        var enrichmentService = new CompanyRegistryEnrichmentService(
            companyRegistryGateway ?? new StubCompanyRegistryGateway(CompanyRegistryLookupStatus.Found),
            companyRepository,
            Options.Create(new CompanyRegistryOptions()),
            NullLogger<CompanyRegistryEnrichmentService>.Instance);
        var corporateMemberService = new CorporateMemberService(
            corporateMemberRepository,
            companyRepository);

        return new TestCatalog(
            new CompanyCatalogService(
                companyRepository,
                companyCatalogImportRepository,
                corporateMemberRepository,
                corporateMemberService,
                companyBillingScheduleRepository,
                billingDraftRepository,
                enrichmentService,
                auditLogRepository),
            new CompanyCatalogImportService(
                new CompanyCatalogSpreadsheetReader(new SpreadsheetWorkbookReader()),
                companyRepository,
                companyCatalogImportRepository,
                corporateMemberService),
            dataStore);
    }

    private sealed record TestCatalog(
        CompanyCatalogService Service,
        CompanyCatalogImportService ImportService,
        InMemoryBillingDataStore DataStore);

    /// <summary>
    /// Substitui a BrasilAPI nos testes. Nenhum teste depende do serviço real.
    /// </summary>
    private sealed class StubCompanyRegistryGateway(CompanyRegistryLookupStatus lookupStatus)
        : ICompanyRegistryGateway
    {
        public Task<CompanyRegistryLookupResult> FindByTaxIdAsync(
            string taxId,
            CancellationToken cancellationToken)
        {
            var lookupResult = lookupStatus switch
            {
                CompanyRegistryLookupStatus.Found => FindRegisteredCompany(taxId),
                CompanyRegistryLookupStatus.NotFound => CompanyRegistryLookupResult.NotFound(),
                _ => CompanyRegistryLookupResult.Unavailable(),
            };
            return Task.FromResult(lookupResult);
        }

        /// <summary>
        /// Dados reais conferidos na BrasilAPI para os dois CNPJs usados nos
        /// testes, para que cada empresa tenha um cadastro distinto.
        /// </summary>
        private static CompanyRegistryLookupResult FindRegisteredCompany(string taxId)
        {
            return taxId == OpenSportsTaxId
                ? CompanyRegistryLookupResult.Found(
                    "OPEN SPORTS LTDA",
                    "OPEN SPORTS",
                    "ATIVA",
                    new CompanyRegistryAddress(
                        "AVENIDA PERIMETRAL NORTE",
                        "8303",
                        "SALA 04",
                        "FAZ CRIMEIA CAVEIRAS",
                        "GOIANIA",
                        "GO",
                        "74593841"))
                : CompanyRegistryLookupResult.Found(
                    "WEB PRADO CONSULTORIA EM MARKETING E TREINAMENTO LTDA",
                    "WEB PRADO",
                    "ATIVA",
                    new CompanyRegistryAddress(
                        "RUA DAS FIGUEIRAS",
                        "1200",
                        string.Empty,
                        "JARDIM",
                        "SANTO ANDRE",
                        "SP",
                        "09080370"));
        }
    }

    private sealed class ThrowingCompanyRegistryGateway : ICompanyRegistryGateway
    {
        public Task<CompanyRegistryLookupResult> FindByTaxIdAsync(
            string taxId,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Falha externa simulada.");
        }
    }
}
