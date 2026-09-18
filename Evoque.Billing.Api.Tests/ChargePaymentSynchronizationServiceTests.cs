using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargePaymentSynchronizationServiceTests
{
    private const string OperatorId = "maria";
    private const string CompanyTaxId = "02346076000107";

    /// <summary>
    /// Checar só o campo em memória não prova persistência: o InMemory devolve a
    /// mesma referência que <c>ApplyPaymentStatus</c> já mutou antes de qualquer
    /// decisão de gravar. A prova é <c>UpdateAsync</c> ter sido chamado, com o
    /// mesmo <see cref="CountingChargeBatchRepository"/> usado nos demais testes,
    /// e a leitura de conferência vem de uma nova consulta ao repositório.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_StoresAndPersistsWhatAsaasReturned()
    {
        var scenario = await CreateSingleChargeScenarioAsync(
            asaasPaymentId: "pay_farmava",
            gatewayResponses: new Dictionary<string, AsaasChargeState>
            {
                ["pay_farmava"] = new("pay_farmava", "RECEIVED", new DateOnly(2026, 9, 20)),
            });
        var updateCallCountBeforeSync = scenario.ChargeBatchRepository.UpdateCallCount;

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(updateCallCountBeforeSync + 1, scenario.ChargeBatchRepository.UpdateCallCount);
        var persistedBatch = await scenario.ChargeBatchRepository.FindByIdAsync(
            scenario.ChargeBatchId,
            CancellationToken.None);
        var persistedItem = Assert.Single(persistedBatch!.Items);
        Assert.Equal(ChargePaymentStatus.Received, persistedItem.PaymentStatus);
        Assert.Equal(new DateOnly(2026, 9, 20), persistedItem.PaidAt);
    }

    /// <summary>
    /// Uma cobrança assentada não muda mais e não pode gerar tráfego
    /// desnecessário contra o Asaas a cada clique em "Atualizar situação".
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_DoesNotQueryAnAlreadyPaidCharge()
    {
        var scenario = await CreateSingleChargeScenarioAsync(
            asaasPaymentId: "pay_farmava",
            gatewayResponses: new Dictionary<string, AsaasChargeState>
            {
                ["pay_farmava"] = new("pay_farmava", "RECEIVED", new DateOnly(2026, 9, 20)),
            });
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);
        Assert.Equal(1, scenario.Gateway.CallCount);

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(1, scenario.Gateway.CallCount);
    }

    /// <summary>
    /// Uma indisponibilidade externa numa cobrança não pode apagar o que já
    /// sabemos dela nem impedir a consulta das demais cobranças da mesma
    /// chamada.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_KeepsWhatIsKnownAndDoesNotStopTheOthersWhenAQueryFails()
    {
        var scenario = await CreateTwoChargeScenarioAsync(
            firstAsaasPaymentId: "pay_fails",
            secondAsaasPaymentId: "pay_succeeds",
            gatewayResponses: new Dictionary<string, AsaasChargeState>
            {
                ["pay_succeeds"] = new("pay_succeeds", "RECEIVED", new DateOnly(2026, 9, 20)),
            },
            paymentIdsThatThrow: ["pay_fails"]);

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        var persistedBatch = await scenario.ChargeBatchRepository.FindByIdAsync(
            scenario.ChargeBatchId,
            CancellationToken.None);
        var failedItem = persistedBatch!.Items.Single(item => item.AsaasPaymentId == "pay_fails");
        var succeededItem = persistedBatch.Items.Single(item => item.AsaasPaymentId == "pay_succeeds");
        Assert.Equal(ChargePaymentStatus.Unknown, failedItem.PaymentStatus);
        Assert.Equal(ChargePaymentStatus.Received, succeededItem.PaymentStatus);

        var auditLogs = await scenario.AuditLogRepository.ListByBillingDraftIdAsync(
            scenario.FirstBillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "charge-payment.query-failed");
    }

    /// <summary>
    /// Um item que falhou ao criar a cobrança não tem <c>AsaasPaymentId</c>: não
    /// há o que consultar no Asaas.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_SkipsAnItemWithNoCharge()
    {
        var scenario = await CreateSingleChargeScenarioAsync(
            asaasPaymentId: null,
            gatewayResponses: []);

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(0, scenario.Gateway.CallCount);
    }

    /// <summary>
    /// Regressão análoga à de <c>FiscalInvoiceService</c>: uma resposta que só
    /// traz a data de pagamento, sem mudar o status, não pode ser descartada. A
    /// prova precisa ser <c>UpdateAsync</c> chamado, não o campo em memória, pois
    /// o InMemory devolve a mesma referência que <c>ApplyPaymentStatus</c> já
    /// mutou.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_PersistsANewlyArrivedPaidDateEvenWhenTheStatusDidNotChange()
    {
        var scenario = await CreateSingleChargeScenarioAsync(
            asaasPaymentId: "pay_farmava",
            gatewayResponses: new Dictionary<string, AsaasChargeState>
            {
                ["pay_farmava"] = new("pay_farmava", "OVERDUE", null),
            });
        // Primeira sincronização: chega o status Overdue, sem data de pagamento.
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);
        var updateCallCountAfterFirstSync = scenario.ChargeBatchRepository.UpdateCallCount;

        // Segunda sincronização: o Asaas devolve o mesmo status Overdue, mas
        // agora com a data de pagamento preenchida.
        scenario.Gateway.SetResponse("pay_farmava", new AsaasChargeState("pay_farmava", "OVERDUE", new DateOnly(2026, 9, 22)));
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(updateCallCountAfterFirstSync + 1, scenario.ChargeBatchRepository.UpdateCallCount);
        var persistedBatch = await scenario.ChargeBatchRepository.FindByIdAsync(
            scenario.ChargeBatchId,
            CancellationToken.None);
        var persistedItem = Assert.Single(persistedBatch!.Items);
        Assert.Equal(ChargePaymentStatus.Overdue, persistedItem.PaymentStatus);
        Assert.Equal(new DateOnly(2026, 9, 22), persistedItem.PaidAt);
    }

    private static async Task<SingleChargeScenario> CreateSingleChargeScenarioAsync(
        string? asaasPaymentId,
        Dictionary<string, AsaasChargeState> gatewayResponses)
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new CountingChargeBatchRepository(new InMemoryChargeBatchRepository(dataStore));
        var chargeHistoryRepository = new InMemoryChargeHistoryRepository(dataStore);
        var auditLogRepository = new InMemoryAuditLogRepository(dataStore);
        var gateway = new StubAsaasChargeGateway(gatewayResponses);

        var billingPeriod = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(billingPeriod, CancellationToken.None);

        var issuedAt = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var billingDraft = new BillingDraft(
            billingPeriod.Id, CompanyTaxId, "Farmava", CompanyTaxId, "cus_teste",
            [new BillingDraftItem("Plano corporativo", 1, 299.50m, "m1")],
            issuedAt);
        billingDraft.Approve(OperatorId, issuedAt);
        await billingDraftRepository.AddAsync(billingDraft, CancellationToken.None);

        var chargeBatch = new ChargeBatch(
            billingPeriod.Id, new DateOnly(2026, 9, 28), OperatorId, AsaasEnvironment.Sandbox, null,
            [billingDraft.Id], issuedAt);
        chargeBatch.Approve(OperatorId, issuedAt);
        chargeBatch.StartProcessing(issuedAt);
        if (asaasPaymentId is null)
        {
            chargeBatch.GetItem(billingDraft.Id).MarkFailed("Falha simulada na criação.", issuedAt);
        }
        else
        {
            chargeBatch.GetItem(billingDraft.Id).MarkChargeCreated(
                asaasPaymentId, $"https://asaas/boleto/{asaasPaymentId}", true, issuedAt);
        }

        chargeBatch.MarkCompleted(issuedAt);
        await chargeBatchRepository.AddAsync(chargeBatch, CancellationToken.None);

        var service = new ChargePaymentSynchronizationService(
            chargeBatchRepository, chargeHistoryRepository, gateway, auditLogRepository);

        return new SingleChargeScenario(service, chargeBatchRepository, auditLogRepository, gateway, chargeBatch.Id);
    }

    private static async Task<TwoChargeScenario> CreateTwoChargeScenarioAsync(
        string firstAsaasPaymentId,
        string secondAsaasPaymentId,
        Dictionary<string, AsaasChargeState> gatewayResponses,
        IReadOnlyCollection<string> paymentIdsThatThrow)
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new CountingChargeBatchRepository(new InMemoryChargeBatchRepository(dataStore));
        var chargeHistoryRepository = new InMemoryChargeHistoryRepository(dataStore);
        var auditLogRepository = new InMemoryAuditLogRepository(dataStore);
        var gateway = new StubAsaasChargeGateway(gatewayResponses, paymentIdsThatThrow);

        var billingPeriod = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(billingPeriod, CancellationToken.None);

        var issuedAt = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var firstBillingDraft = new BillingDraft(
            billingPeriod.Id, CompanyTaxId, "Farmava", CompanyTaxId, "cus_teste",
            [new BillingDraftItem("Plano corporativo", 1, 299.50m, "m1")],
            issuedAt);
        firstBillingDraft.Approve(OperatorId, issuedAt);
        await billingDraftRepository.AddAsync(firstBillingDraft, CancellationToken.None);

        var secondBillingDraft = new BillingDraft(
            billingPeriod.Id, "56087276000103", "Open Sports", "56087276000103", "cus_teste_2",
            [new BillingDraftItem("Plano corporativo", 1, 199.90m, "m2")],
            issuedAt);
        secondBillingDraft.Approve(OperatorId, issuedAt);
        await billingDraftRepository.AddAsync(secondBillingDraft, CancellationToken.None);

        var chargeBatch = new ChargeBatch(
            billingPeriod.Id, new DateOnly(2026, 9, 28), OperatorId, AsaasEnvironment.Sandbox, null,
            [firstBillingDraft.Id, secondBillingDraft.Id], issuedAt);
        chargeBatch.Approve(OperatorId, issuedAt);
        chargeBatch.StartProcessing(issuedAt);
        chargeBatch.GetItem(firstBillingDraft.Id).MarkChargeCreated(
            firstAsaasPaymentId, $"https://asaas/boleto/{firstAsaasPaymentId}", true, issuedAt);
        chargeBatch.GetItem(secondBillingDraft.Id).MarkChargeCreated(
            secondAsaasPaymentId, $"https://asaas/boleto/{secondAsaasPaymentId}", true, issuedAt);
        chargeBatch.MarkCompleted(issuedAt);
        await chargeBatchRepository.AddAsync(chargeBatch, CancellationToken.None);

        var service = new ChargePaymentSynchronizationService(
            chargeBatchRepository, chargeHistoryRepository, gateway, auditLogRepository);

        return new TwoChargeScenario(
            service, chargeBatchRepository, auditLogRepository, gateway, chargeBatch.Id, firstBillingDraft.Id);
    }

    private sealed record SingleChargeScenario(
        ChargePaymentSynchronizationService Service,
        CountingChargeBatchRepository ChargeBatchRepository,
        InMemoryAuditLogRepository AuditLogRepository,
        StubAsaasChargeGateway Gateway,
        Guid ChargeBatchId);

    private sealed record TwoChargeScenario(
        ChargePaymentSynchronizationService Service,
        CountingChargeBatchRepository ChargeBatchRepository,
        InMemoryAuditLogRepository AuditLogRepository,
        StubAsaasChargeGateway Gateway,
        Guid ChargeBatchId,
        Guid FirstBillingDraftId);

    /// <summary>
    /// Conta chamadas a <see cref="IChargeBatchRepository.UpdateAsync"/> sem
    /// mudar o comportamento de persistência. É a forma correta de provar que
    /// <c>SynchronizeAsync</c> gravou (ou não gravou) o lote: o objeto em
    /// memória é compartilhado por referência, então checar os campos no
    /// repositório InMemory não pega a ausência da gravação.
    /// </summary>
    private sealed class CountingChargeBatchRepository(IChargeBatchRepository innerRepository)
        : IChargeBatchRepository
    {
        public int UpdateCallCount { get; private set; }

        public Task AddAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
        {
            return innerRepository.AddAsync(chargeBatch, cancellationToken);
        }

        public Task<ChargeBatch?> FindByIdAsync(Guid chargeBatchId, CancellationToken cancellationToken)
        {
            return innerRepository.FindByIdAsync(chargeBatchId, cancellationToken);
        }

        public Task<IReadOnlyCollection<ChargeBatch>> ListByBillingPeriodIdAsync(
            Guid billingPeriodId,
            CancellationToken cancellationToken)
        {
            return innerRepository.ListByBillingPeriodIdAsync(billingPeriodId, cancellationToken);
        }

        public Task UpdateAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            return innerRepository.UpdateAsync(chargeBatch, cancellationToken);
        }
    }

    private sealed class StubAsaasChargeGateway(
        Dictionary<string, AsaasChargeState> responsesByPaymentId,
        IReadOnlyCollection<string>? paymentIdsThatThrow = null) : IAsaasChargeGateway
    {
        private readonly HashSet<string> paymentIdsThatThrow = [.. paymentIdsThatThrow ?? []];

        public int CallCount { get; private set; }

        public void SetResponse(string asaasPaymentId, AsaasChargeState state)
        {
            responsesByPaymentId[asaasPaymentId] = state;
        }

        public Task<AsaasChargeCreation> CreateChargeAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasChargeRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Não usado por este serviço de sincronização.");
        }

        public Task<AsaasChargeState> GetChargeAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasPaymentId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (paymentIdsThatThrow.Contains(asaasPaymentId))
            {
                throw new InvalidOperationException("Falha simulada de rede.");
            }

            return Task.FromResult(responsesByPaymentId[asaasPaymentId]);
        }
    }
}
