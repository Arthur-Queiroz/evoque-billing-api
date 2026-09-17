using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargeHistoryServiceTests
{
    private const string OperatorId = "maria";
    private const string FarmavaTaxId = "02346076000107";
    private const string OpenSportsTaxId = "56087276000103";

    [Fact]
    public async Task ListAsync_ReturnsTheMostRecentFirstAcrossCompetencies()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(new ChargeHistoryQuery(), CancellationToken.None);

        Assert.Equal(2, historico.Count);
        Assert.Equal("Open Sports", historico.First().CompanyName);
        Assert.Equal("Farmava", historico.Last().CompanyName);
    }

    [Fact]
    public async Task ListAsync_FindsACompanyByName()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(historico).CompanyName);
    }

    [Fact]
    public async Task ListAsync_FindsACompanyByTaxId()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: FarmavaTaxId),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(historico).CompanyName);
    }

    /// <summary>
    /// A regra de dobra de acento é o ponto mais frágil entre a implementação em
    /// memória e a futura implementação MySQL: o banco ganha isso de graça pela
    /// collation `utf8mb4_0900_ai_ci`, e este teste é o que impede a versão em
    /// memória de divergir dela.
    /// </summary>
    [Fact]
    public async Task ListAsync_FindsACompanyByNameWithoutAccentsOrCase()
    {
        var scenario = await CreateScenarioWithAnAccentedCompanyNameAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmacia acucar"),
            CancellationToken.None);

        Assert.Equal("Farmácia Açúcar", Assert.Single(historico).CompanyName);
    }

    /// <summary>
    /// Boleto de teste ao lado de um real, sem distinção, é como se confunde os
    /// dois. O ambiente separa e é filtrável.
    /// </summary>
    [Fact]
    public async Task ListAsync_SeparatesTestFromRealCharges()
    {
        var scenario = await CreateScenarioAsync();

        var teste = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Environment: "Sandbox"),
            CancellationToken.None);
        var real = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Environment: "Production"),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(teste).CompanyName);
        Assert.Equal("Open Sports", Assert.Single(real).CompanyName);
    }

    [Fact]
    public async Task ListAsync_ShowsTheInvoiceWhenThereIsOne()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Equal("Authorized", linha.FiscalInvoiceStatus);
        Assert.Equal("https://asaas/nota.pdf", linha.FiscalInvoicePdfUrl);
    }

    [Fact]
    public async Task ListAsync_LeavesTheInvoiceEmptyWhenThereIsNone()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "open"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Null(linha.FiscalInvoiceStatus);
        Assert.Null(linha.FiscalInvoicePdfUrl);
    }

    /// <summary>
    /// Monta duas cobranças emitidas: a Farmava em Sandbox, com nota, e a Open
    /// Sports em Produção, sem nota, uma competência depois.
    /// </summary>
    private static async Task<TestScenario> CreateScenarioAsync()
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);
        var fiscalInvoiceRepository = new InMemoryFiscalInvoiceRepository(dataStore);

        var setembro = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        var outubro = new BillingPeriod(new BillingPeriodReference(2026, 10), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(setembro, CancellationToken.None);
        await billingPeriodRepository.AddAsync(outubro, CancellationToken.None);

        var farmava = await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            setembro, FarmavaTaxId, "Farmava", 299.50m, AsaasEnvironment.Sandbox,
            new DateOnly(2026, 9, 28), "pay_farmava", new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));

        await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            outubro, OpenSportsTaxId, "Open Sports", 2157.60m, AsaasEnvironment.Production,
            new DateOnly(2026, 10, 25), "pay_open", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var nota = new FiscalInvoice(
            farmava, setembro.Id, 1, AsaasEnvironment.Sandbox, "pay_farmava",
            299.50m, new DateOnly(2026, 9, 28), false,
            "Serviços prestados em 09/2026.", DateTimeOffset.UtcNow);
        nota.MarkScheduled("inv_farmava", DateTimeOffset.UtcNow);
        nota.ApplyAsaasStatus("AUTHORIZED", null, DateTimeOffset.UtcNow);
        nota.AttachDocuments("https://asaas/nota.pdf", "https://asaas/nota.xml", DateTimeOffset.UtcNow);
        await fiscalInvoiceRepository.AddAsync(nota, CancellationToken.None);

        return new TestScenario(
            new ChargeHistoryService(new InMemoryChargeHistoryRepository(dataStore)),
            dataStore);
    }

    /// <summary>
    /// Cenário dedicado e menor, só para a regra de dobra de acento: uma única
    /// empresa cujo nome tem acento, sem nota fiscal envolvida.
    /// </summary>
    private static async Task<TestScenario> CreateScenarioWithAnAccentedCompanyNameAsync()
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);

        var setembro = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(setembro, CancellationToken.None);

        await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            setembro, "12345678000195", "Farmácia Açúcar", 150.00m, AsaasEnvironment.Sandbox,
            new DateOnly(2026, 9, 28), "pay_farmacia_acucar", new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));

        return new TestScenario(
            new ChargeHistoryService(new InMemoryChargeHistoryRepository(dataStore)),
            dataStore);
    }

    private static async Task<Guid> CriarCobrancaAsync(
        InMemoryBillingDataStore dataStore,
        InMemoryBillingDraftRepository billingDraftRepository,
        InMemoryChargeBatchRepository chargeBatchRepository,
        BillingPeriod billingPeriod,
        string companyTaxId,
        string companyName,
        decimal amount,
        AsaasEnvironment asaasEnvironment,
        DateOnly dueDate,
        string asaasPaymentId,
        DateTimeOffset issuedAt)
    {
        var billingDraft = new BillingDraft(
            billingPeriod.Id, companyTaxId, companyName, companyTaxId, "cus_teste",
            [new BillingDraftItem("Plano corporativo", 1, amount, "m1")],
            issuedAt);
        billingDraft.Approve(OperatorId, issuedAt);
        await billingDraftRepository.AddAsync(billingDraft, CancellationToken.None);

        var chargeBatch = new ChargeBatch(
            billingPeriod.Id, dueDate, OperatorId, asaasEnvironment, null,
            [billingDraft.Id], issuedAt);
        chargeBatch.Approve(OperatorId, issuedAt);
        chargeBatch.StartProcessing(issuedAt);
        chargeBatch.GetItem(billingDraft.Id).MarkChargeCreated(
            asaasPaymentId, $"https://asaas/boleto/{asaasPaymentId}", true, issuedAt);
        chargeBatch.MarkCompleted(issuedAt);
        await chargeBatchRepository.AddAsync(chargeBatch, CancellationToken.None);

        return billingDraft.Id;
    }

    private sealed record TestScenario(
        ChargeHistoryService Service,
        InMemoryBillingDataStore DataStore);
}
