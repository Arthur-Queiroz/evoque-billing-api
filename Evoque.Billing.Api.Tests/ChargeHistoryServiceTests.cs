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
    /// Nome e CNPJ são buscas mutuamente exclusivas pela forma do termo. Sem essa
    /// regra, o dígito solto em "Farmava 2" virava candidato a CNPJ e casava com
    /// qualquer empresa cujo CNPJ contivesse aquele dígito — aqui, as duas, pois
    /// os dois CNPJs de teste têm um "2" em algum lugar.
    /// </summary>
    [Fact]
    public async Task ListAsync_DoesNotTreatADigitInsideANameSearchAsATaxId()
    {
        var scenario = await CreateScenarioAsync();

        // A regressão só é exercitada enquanto os dois CNPJs contiverem o dígito
        // buscado. Trocar um deles por outro sem "2" faria este teste continuar
        // verde sem provar mais nada, então a precondição falha alto.
        Assert.Contains("2", FarmavaTaxId, StringComparison.Ordinal);
        Assert.Contains("2", OpenSportsTaxId, StringComparison.Ordinal);

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "Farmava 2"),
            CancellationToken.None);

        Assert.Empty(historico);
    }

    /// <summary>
    /// Um termo feito só de pontuação de CNPJ, sem nenhum dígito (ex.: "-"),
    /// não pode ser tratado como busca por CNPJ: <c>SearchesByTaxId</c> exige
    /// ao menos um dígito. Sem essa exigência, o termo era CNPJ-shaped com zero
    /// dígitos, e as duas implementações discordavam sobre o que fazer com uma
    /// extração vazia — a de memória devolvia lista vazia e a MySQL, por casar
    /// `LIKE '%%'` com tudo, devolveria o histórico inteiro. Este teste finca a
    /// interpretação correta: um termo assim é busca por nome.
    /// </summary>
    [Fact]
    public async Task ListAsync_TreatsAPunctuationOnlySearchAsAName()
    {
        var scenario = await CreateScenarioWithAHyphenatedCompanyNameAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "-"),
            CancellationToken.None);

        Assert.Equal("Farmava - Matriz", Assert.Single(historico).CompanyName);
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

    [Fact]
    public async Task ListAsync_RejectsAnUnrecognizedEnvironment()
    {
        var scenario = await CreateScenarioAsync();

        await Assert.ThrowsAsync<ValidationException>(() => scenario.Service.ListAsync(
            new ChargeHistoryQuery(Environment: "Producao"),
            CancellationToken.None));
    }

    /// <summary>
    /// Ano e mês são exigidos juntos: um filtro parcial não tem competência
    /// nenhuma para comparar. Este cobre o ano sem o mês; o teste seguinte cobre
    /// o mês sem o ano, porque são dois ramos distintos em
    /// <c>ChargeHistoryService.ParseBillingPeriodReference</c>.
    /// </summary>
    [Fact]
    public async Task ListAsync_RequiresMonthWhenYearIsProvided()
    {
        var scenario = await CreateScenarioAsync();

        await Assert.ThrowsAsync<ValidationException>(() => scenario.Service.ListAsync(
            new ChargeHistoryQuery(Year: 2026),
            CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_RequiresYearWhenMonthIsProvided()
    {
        var scenario = await CreateScenarioAsync();

        await Assert.ThrowsAsync<ValidationException>(() => scenario.Service.ListAsync(
            new ChargeHistoryQuery(Month: 9),
            CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByCompetencyWhenYearAndMonthAreBothProvided()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Year: 2026, Month: 9),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(historico).CompanyName);
    }

    [Fact]
    public async Task ListAsync_ShowsWhetherTheSlipWasPaid()
    {
        var scenario = await CreateScenarioAsync();
        var chargeBatch = scenario.DataStore.ChargeBatches.Values
            .Single(batch => batch.AsaasEnvironment == AsaasEnvironment.Sandbox);
        chargeBatch.Items.Single().ApplyPaymentStatus(
            "RECEIVED",
            new DateOnly(2026, 10, 2),
            DateTimeOffset.UtcNow);

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Equal("Received", linha.PaymentStatus);
        Assert.Equal(new DateOnly(2026, 10, 2), linha.PaidAt);
    }

    /// <summary>
    /// Uma cobrança que ninguém consultou não é uma cobrança em aberto. A tela
    /// precisa dizer "não consultado", e para isso o estado tem que chegar nela
    /// distinto de `Pending`.
    /// </summary>
    [Fact]
    public async Task ListAsync_SaysNobodyHasAskedYetInsteadOfGuessing()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "open"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Equal("Unknown", linha.PaymentStatus);
        Assert.Null(linha.PaidAt);
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

    /// <summary>
    /// Cenário dedicado à regressão de termo só-pontuação: uma única empresa
    /// cujo nome contém um hífen, sem nota fiscal envolvida.
    /// </summary>
    private static async Task<TestScenario> CreateScenarioWithAHyphenatedCompanyNameAsync()
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);

        var setembro = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(setembro, CancellationToken.None);

        await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            setembro, "12345678000195", "Farmava - Matriz", 150.00m, AsaasEnvironment.Sandbox,
            new DateOnly(2026, 9, 28), "pay_farmava_matriz", new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));

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
