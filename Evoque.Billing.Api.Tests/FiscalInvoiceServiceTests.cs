using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Tests;

public sealed class FiscalInvoiceServiceTests
{
    private const string CompanyTaxIdValue = "45423360000134";
    private const string OperatorId = "maria";

    [Fact]
    public async Task ExecuteAsync_IssuesOneInvoicePerChargeCreatedInProduction()
    {
        var context = await CreateApprovedDraftAsync();

        await ExecuteProductionBatchAsync(context);

        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Scheduled, fiscalInvoice.Status);
        Assert.Equal("inv_000022573933", fiscalInvoice.AsaasInvoiceId);
        Assert.Equal(1, context.InvoiceGateway.ScheduleCallCount);

        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.scheduled");
    }

    /// <summary>
    /// O valor da nota é o da prévia aprovada. Juros e multa de boleto atrasado
    /// não são serviço prestado e não podem ser tributados como tal.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SendsTheApprovedDraftAmountAndTheConfiguredService()
    {
        var context = await CreateApprovedDraftAsync();

        await ExecuteProductionBatchAsync(context);

        var request = Assert.Single(context.InvoiceGateway.ScheduledRequests);
        Assert.Equal(209.70m, request.Value);
        Assert.Equal("82367", request.MunicipalServiceId);
        Assert.Equal("Serviços prestados em 08/2026", request.MunicipalServiceName);
        Assert.Equal("Serviços prestados em 08/2026.", request.ServiceDescription);
        Assert.Equal(5.00m, request.IssTaxRate);
        Assert.False(request.RetainsIss);
        Assert.Contains("billing-draft:", request.ExternalReference);
    }

    [Fact]
    public async Task ExecuteAsync_SendsIssRetentionForACompanyThatRequiresIt()
    {
        var context = await CreateApprovedDraftAsync(retainsIss: true);

        await ExecuteProductionBatchAsync(context);

        var request = Assert.Single(context.InvoiceGateway.ScheduledRequests);
        Assert.True(request.RetainsIss);
    }

    /// <summary>
    /// A segunda chamada não passa pelo lote de novo: uma vez com cobrança
    /// criada, a prévia deixa de estar "Approved" e um segundo lote para ela é
    /// recusado por uma regra do próprio lote, sem relação com a nota fiscal.
    /// O que este teste cobre é a idempotência do <see cref="FiscalInvoiceService"/>
    /// em si: chamar <c>IssueForChargeAsync</c> de novo para a mesma prévia, como
    /// aconteceria se o lote fosse executado de novo sobre o mesmo item.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DoesNotIssueASecondInvoiceForTheSameDraft()
    {
        var context = await CreateApprovedDraftAsync();
        var chargeBatch = await ExecuteProductionBatchAsync(context);
        var asaasPaymentId = Assert.Single(chargeBatch.Items).AsaasPaymentId!;

        await context.FiscalInvoiceService.IssueForChargeAsync(
            context.BillingDraftId,
            asaasPaymentId,
            OperatorId,
            AsaasEnvironment.Production,
            CancellationToken.None);

        Assert.Single(await context.FiscalInvoiceRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None));
        Assert.Equal(1, context.InvoiceGateway.ScheduleCallCount);
    }

    /// <summary>
    /// O Sandbox não emite NFS-e de verdade; tentar emitir lá só produziria ruído.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkipsTheInvoiceInSandbox()
    {
        var context = await CreateApprovedDraftAsync();

        await context.ChargeBatchService.CreateAsync(
            new CreateChargeBatchRequest(
                OperatorId,
                new DateOnly(2026, 9, 10),
                "CONFIRMAR",
                [context.BillingDraftId]),
            CancellationToken.None);

        Assert.Empty(await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
            context.BillingPeriodId,
            CancellationToken.None));
        Assert.Equal(0, context.InvoiceGateway.ScheduleCallCount);

        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.skipped-sandbox");
    }

    /// <summary>
    /// A cobrança é o dinheiro; a nota é etapa fiscal. Recusa da prefeitura não
    /// pode desfazer nem esconder o boleto que já foi criado.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_KeepsTheChargeWhenTheInvoiceIsRefused()
    {
        var context = await CreateApprovedDraftAsync(
            invoiceGateway: new RecordingAsaasInvoiceGateway(
                scheduleFailureMessage: "Conforme legislação municipal, deve ter retenção de ISS."));

        var chargeBatch = await ExecuteProductionBatchAsync(context);

        Assert.Equal("Completed", chargeBatch.Status);
        Assert.Equal("Created", Assert.Single(chargeBatch.Items).Status);

        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Failed, fiscalInvoice.Status);
        Assert.Contains("retenção de ISS", fiscalInvoice.ErrorMessage);

        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.failed");
    }

    /// <summary>
    /// Regressão: uma falha da nota fiscal fora do caminho do gateway (aqui, o
    /// <c>AddAsync</c> do registro "Issuing", como aconteceria numa corrida real
    /// contra a chave única) não pode subir até
    /// <c>ChargeBatchService.ExecuteItemAsync</c> e cair no catch de lá — esse
    /// catch chama <c>ChargeBatchItem.MarkFailed</c>, que apaga
    /// <c>AsaasPaymentId</c> e <c>BankSlipUrl</c> de uma cobrança que já existe
    /// no Asaas e já foi cobrada do cliente. Este é o teste que teria pego o bug.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_KeepsTheChargeItemIntactWhenFiscalInvoiceFailsOutsideTheGateway()
    {
        var context = await CreateApprovedDraftAsync(
            fiscalInvoiceRepository: new FaultyFiscalInvoiceRepository());

        var chargeBatch = await ExecuteProductionBatchAsync(context);

        Assert.Equal("Completed", chargeBatch.Status);
        var item = Assert.Single(chargeBatch.Items);
        Assert.Equal("Created", item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.AsaasPaymentId));

        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.issuance-error");
    }

    private static async Task<ChargeBatchResponse> ExecuteProductionBatchAsync(TestContext context)
    {
        var chargeBatch = await context.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                OperatorId,
                new DateOnly(2026, 9, 10),
                "Production",
                [context.BillingDraftId]),
            CancellationToken.None);
        await context.ChargeBatchService.ApproveAsync(
            chargeBatch.Id,
            new ApproveChargeBatchRequest(OperatorId),
            CancellationToken.None);
        return await context.ChargeBatchService.ExecuteAsync(
            chargeBatch.Id,
            new ExecuteChargeBatchRequest(OperatorId, "CONFIRMAR"),
            CancellationToken.None);
    }

    private static async Task<TestContext> CreateApprovedDraftAsync(
        bool retainsIss = false,
        RecordingAsaasInvoiceGateway? invoiceGateway = null,
        IFiscalInvoiceRepository? fiscalInvoiceRepository = null)
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var auditLogRepository = new InMemoryAuditLogRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);
        var fiscalInvoiceRepositoryToUse = fiscalInvoiceRepository ?? new InMemoryFiscalInvoiceRepository(dataStore);
        var companyRepository = new InMemoryCompanyRepository(dataStore);
        var recordingInvoiceGateway = invoiceGateway ?? new RecordingAsaasInvoiceGateway();

        // Relógio fixo em 01/08/2026: os vencimentos dos cenários (10/09/2026)
        // são datas fixas, e um TimeProvider real quebraria a suíte sozinho
        // assim que o calendário ultrapassasse essas datas.
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));

        var company = Company.CreateManually(
            CompanyTaxIdValue,
            "Ludmax Comercio Eletronico",
            OperatorId,
            DateTimeOffset.UtcNow);
        company.SetIssRetention(retainsIss, OperatorId, DateTimeOffset.UtcNow);
        await companyRepository.UpsertAsync(company, CancellationToken.None);

        var chargeCreationService = new ChargeCreationService(
            billingPeriodRepository,
            billingDraftRepository,
            auditLogRepository,
            new AlwaysReadyNotificationGateway(),
            new StubAsaasChargeGateway(),
            timeProvider);
        var fiscalInvoiceService = new FiscalInvoiceService(
            billingPeriodRepository,
            billingDraftRepository,
            fiscalInvoiceRepositoryToUse,
            companyRepository,
            auditLogRepository,
            recordingInvoiceGateway,
            Options.Create(new FiscalInvoiceOptions
            {
                MunicipalServiceId = "82367",
                IssTaxRate = 5.00m,
            }),
            timeProvider);
        var chargeBatchService = new ChargeBatchService(
            billingPeriodRepository,
            billingDraftRepository,
            chargeBatchRepository,
            auditLogRepository,
            chargeCreationService,
            fiscalInvoiceService,
            timeProvider);

        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        var billingPeriodService = new BillingPeriodService(billingPeriodRepository, auditLogRepository);
        var billingDraftService = new BillingDraftService(
            billingPeriodRepository,
            billingDraftRepository,
            auditLogRepository);
        await billingPeriodService.CreateAsync(billingPeriodReference, OperatorId, CancellationToken.None);
        var billingDraft = await billingDraftService.CreateAsync(
            billingPeriodReference,
            new CreateBillingDraftCommand(
                CompanyTaxIdValue,
                "Ludmax Comercio Eletronico",
                CompanyTaxIdValue,
                "cus_000153431495",
                [
                    new CreateBillingDraftItemCommand("Plano corporativo", 2, 79.90m, "member-1"),
                    new CreateBillingDraftItemCommand("Dependente", 1, 49.90m, "member-2"),
                ]),
            OperatorId,
            CancellationToken.None);
        await billingDraftService.ApproveAsync(billingDraft.Id, OperatorId, CancellationToken.None);
        var billingPeriod = await billingPeriodService.GetByReferenceAsync(
            billingPeriodReference,
            CancellationToken.None);

        return new TestContext(
            chargeBatchService,
            fiscalInvoiceService,
            fiscalInvoiceRepositoryToUse,
            auditLogRepository,
            recordingInvoiceGateway,
            billingDraft.Id,
            billingPeriod.Id);
    }

    private sealed record TestContext(
        ChargeBatchService ChargeBatchService,
        FiscalInvoiceService FiscalInvoiceService,
        IFiscalInvoiceRepository FiscalInvoiceRepository,
        InMemoryAuditLogRepository AuditLogRepository,
        RecordingAsaasInvoiceGateway InvoiceGateway,
        Guid BillingDraftId,
        Guid BillingPeriodId);

    private sealed class RecordingAsaasInvoiceGateway(
        string? scheduleFailureMessage = null,
        string statusOnGet = "AUTHORIZED") : IAsaasInvoiceGateway
    {
        private readonly List<AsaasInvoiceRequest> scheduledRequests = [];

        public int ScheduleCallCount { get; private set; }

        public IReadOnlyList<AsaasInvoiceRequest> ScheduledRequests => scheduledRequests;

        public Task<AsaasInvoiceCreation> ScheduleInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasInvoiceRequest request,
            CancellationToken cancellationToken)
        {
            ScheduleCallCount++;
            if (scheduleFailureMessage is not null)
            {
                throw new ExternalOperationNotAllowedException(scheduleFailureMessage);
            }

            scheduledRequests.Add(request);
            return Task.FromResult(new AsaasInvoiceCreation("inv_000022573933", "SCHEDULED"));
        }

        public Task<AsaasInvoiceState> GetInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasInvoiceId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasInvoiceState(asaasInvoiceId, statusOnGet, null));
        }
    }

    /// <summary>
    /// Simula a corrida que a chave única (billing_draft_id, sequence) existe
    /// para barrar: o <c>AddAsync</c> do registro "Issuing" falha antes de
    /// qualquer chamada ao gateway. Usado para provar que essa falha nunca
    /// alcança o item do lote de cobrança.
    /// </summary>
    private sealed class FaultyFiscalInvoiceRepository : IFiscalInvoiceRepository
    {
        public Task AddAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
        {
            throw new ConflictException("Já existe uma nota fiscal com essa sequência para a prévia.");
        }

        public Task UpdateAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Não esperado: AddAsync sempre falha antes de qualquer atualização.");
        }

        public Task<FiscalInvoice?> FindByIdAsync(Guid fiscalInvoiceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<FiscalInvoice?>(null);
        }

        public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingDraftIdAsync(
            Guid billingDraftId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<FiscalInvoice>>([]);
        }

        public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingPeriodIdAsync(
            Guid billingPeriodId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<FiscalInvoice>>([]);
        }
    }

    private sealed class StubAsaasChargeGateway : IAsaasChargeGateway
    {
        public Task<AsaasChargeCreation> CreateChargeAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasChargeRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasChargeCreation(
                "pay_7h844wckdkflengp",
                "https://www.asaas.com/pdf/pay_7h844wckdkflengp"));
        }
    }

    private sealed class AlwaysReadyNotificationGateway : IAsaasCustomerNotificationGateway
    {
        public Task<AsaasCustomerEmailDeliveryReadiness> GetEmailDeliveryReadinessAsync(
            AsaasEnvironment asaasEnvironment,
            string customerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasCustomerEmailDeliveryReadiness(true, true));
        }
    }
}
