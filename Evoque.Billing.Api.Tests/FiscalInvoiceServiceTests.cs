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
    /// Quem decide é a configuração do ambiente, não o nome dele. Um ambiente
    /// com a emissão desligada não tenta emitir e não deixa registro de falha:
    /// a nota simplesmente não faz parte daquele lote.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkipsTheInvoiceWhenIssuanceIsDisabledForTheEnvironment()
    {
        var context = await CreateApprovedDraftAsync(asaasOptions: new AsaasOptions());

        await ExecuteProductionBatchAsync(context);

        Assert.Empty(await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
            context.BillingPeriodId,
            CancellationToken.None));
        Assert.Equal(0, context.InvoiceGateway.ScheduleCallCount);

        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.skipped-disabled");
    }

    /// <summary>
    /// O Sandbox aceita o mesmo POST /v3/invoices e a mesma lista de serviços
    /// municipais da Produção, então é onde a emissão pode ser exercitada antes
    /// de valer na prefeitura. A nota guarda o ambiente em que nasceu para que
    /// sincronização e reemissão não troquem de conta.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_IssuesInSandboxAndKeepsTheEnvironmentOnTheInvoice()
    {
        var context = await CreateApprovedDraftAsync();

        await context.ChargeBatchService.CreateAsync(
            new CreateChargeBatchRequest(
                new DateOnly(2026, 9, 10),
                "CONFIRMAR",
                [context.BillingDraftId]),
            OperatorId,
            CancellationToken.None);

        var fiscalInvoice = Assert.Single(await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
            context.BillingPeriodId,
            CancellationToken.None));
        Assert.Equal(AsaasEnvironment.Sandbox, fiscalInvoice.AsaasEnvironment);
        Assert.Equal(AsaasEnvironment.Sandbox, Assert.Single(context.InvoiceGateway.ScheduledEnvironments));
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

    [Fact]
    public async Task SynchronizeAsync_UpdatesTheStatusOfInvoicesThatAreNotSettled()
    {
        var context = await CreateApprovedDraftAsync();
        await ExecuteProductionBatchAsync(context);

        var fiscalInvoices = await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        Assert.Equal("Authorized", Assert.Single(fiscalInvoices).Status);
        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.Contains(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.status-synchronized");
    }

    /// <summary>
    /// Uma nota assentada (aqui, Failed) não muda mais e não pode gerar tráfego
    /// desnecessário contra o Asaas a cada sincronização da competência.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_DoesNotQueryASettledInvoice()
    {
        var invoiceGateway = new RecordingAsaasInvoiceGateway(
            scheduleFailureMessage: "Conforme legislação municipal, deve ter retenção de ISS.");
        var context = await CreateApprovedDraftAsync(invoiceGateway: invoiceGateway);
        await ExecuteProductionBatchAsync(context);
        var failedInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Failed, failedInvoice.Status);

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(0, invoiceGateway.GetCallCount);
    }

    /// <summary>
    /// Quando o Asaas devolve o mesmo status já gravado, nada mudou: gravar de
    /// novo e auditar de novo seria ruído, não informação.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_DoesNotUpdateOrAuditWhenTheStatusIsUnchanged()
    {
        var invoiceGateway = new RecordingAsaasInvoiceGateway(statusOnGet: "SCHEDULED");
        var countingFiscalInvoiceRepository = new CountingFiscalInvoiceRepository(
            new InMemoryFiscalInvoiceRepository(new InMemoryBillingDataStore()));
        var context = await CreateApprovedDraftAsync(
            invoiceGateway: invoiceGateway,
            fiscalInvoiceRepository: countingFiscalInvoiceRepository);
        await ExecuteProductionBatchAsync(context);
        var scheduledInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Scheduled, scheduledInvoice.Status);
        var updateCallCountBeforeSync = countingFiscalInvoiceRepository.UpdateCallCount;

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, invoiceGateway.GetCallCount);
        Assert.Equal(updateCallCountBeforeSync, countingFiscalInvoiceRepository.UpdateCallCount);
        var auditLogs = await context.AuditLogRepository.ListByBillingDraftIdAsync(
            context.BillingDraftId,
            CancellationToken.None);
        Assert.DoesNotContain(auditLogs, auditLog => auditLog.Action == "fiscal-invoice.status-synchronized");
    }

    /// <summary>
    /// Checar só o campo em memória não prova persistência: o InMemory devolve a
    /// mesma referência que <c>AttachDocuments</c> já mutou antes de qualquer
    /// decisão de gravar. A prova é <c>UpdateAsync</c> ter sido chamado, com o
    /// mesmo <see cref="CountingFiscalInvoiceRepository"/> usado no teste vizinho
    /// de status inalterado.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_StoresTheDocumentsAsaasReturned()
    {
        var countingFiscalInvoiceRepository = new CountingFiscalInvoiceRepository(
            new InMemoryFiscalInvoiceRepository(new InMemoryBillingDataStore()));
        var context = await CreateApprovedDraftAsync(fiscalInvoiceRepository: countingFiscalInvoiceRepository);
        await ExecuteProductionBatchAsync(context);
        context.InvoiceGateway.ReturnDocumentsOnGet("https://asaas/pdf", "https://asaas/xml");
        var updateCallCountBeforeSync = countingFiscalInvoiceRepository.UpdateCallCount;

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(updateCallCountBeforeSync + 1, countingFiscalInvoiceRepository.UpdateCallCount);
        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal("https://asaas/pdf", fiscalInvoice.PdfUrl);
        Assert.Equal("https://asaas/xml", fiscalInvoice.XmlUrl);
    }

    /// <summary>
    /// Regressão: o laço pulava a persistência quando o status não mudava. Se os
    /// documentos chegassem nessa passagem, seriam descartados. A prova precisa
    /// ser <c>UpdateAsync</c> chamado, não o campo em memória: como o InMemory
    /// devolve a mesma referência que <c>AttachDocuments</c> já mutou, checar
    /// <c>PdfUrl</c> sozinho passaria mesmo com o bug antigo.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_StoresDocumentsEvenWhenTheStatusDoesNotChange()
    {
        var countingFiscalInvoiceRepository = new CountingFiscalInvoiceRepository(
            new InMemoryFiscalInvoiceRepository(new InMemoryBillingDataStore()));
        var context = await CreateApprovedDraftAsync(
            invoiceGateway: new RecordingAsaasInvoiceGateway(statusOnGet: "SCHEDULED"),
            fiscalInvoiceRepository: countingFiscalInvoiceRepository);
        await ExecuteProductionBatchAsync(context);
        context.InvoiceGateway.ReturnDocumentsOnGet("https://asaas/pdf", null);
        var updateCallCountBeforeSync = countingFiscalInvoiceRepository.UpdateCallCount;

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(updateCallCountBeforeSync + 1, countingFiscalInvoiceRepository.UpdateCallCount);
        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Scheduled, fiscalInvoice.Status);
        Assert.Equal("https://asaas/pdf", fiscalInvoice.PdfUrl);
    }

    [Fact]
    public async Task ReissueAsync_RequiresTheConfirmationPhrase()
    {
        var context = await CreateApprovedDraftAsync(
            invoiceGateway: new RecordingAsaasInvoiceGateway(
                scheduleFailureMessage: "Conforme legislação municipal, deve ter retenção de ISS."));
        await ExecuteProductionBatchAsync(context);
        var failedInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));

        await Assert.ThrowsAsync<ValidationException>(() => context.FiscalInvoiceService.ReissueAsync(
            failedInvoice.Id,
            new ReissueFiscalInvoiceRequest(""),
            OperatorId,
            CancellationToken.None));
    }

    /// <summary>
    /// O cenário perigoso é a nota autorizada, não a apenas agendada: é dela que
    /// sairia a nota duplicada na prefeitura, sem chance de cancelamento.
    /// </summary>
    [Fact]
    public async Task ReissueAsync_RefusesAnInvoiceThatIsNotFailed()
    {
        var context = await CreateApprovedDraftAsync();
        await ExecuteProductionBatchAsync(context);
        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);
        var authorizedInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Authorized, authorizedInvoice.Status);

        await Assert.ThrowsAsync<ConflictException>(() => context.FiscalInvoiceService.ReissueAsync(
            authorizedInvoice.Id,
            new ReissueFiscalInvoiceRequest("CONFIRMAR"),
            OperatorId,
            CancellationToken.None));
    }

    /// <summary>
    /// É esse caminho que torna útil corrigir a retenção de ISS de uma empresa:
    /// sem ele, a nota recusada pela prefeitura ficaria recusada para sempre.
    /// </summary>
    [Fact]
    public async Task ReissueAsync_CreatesTheNextSequenceWithTheCurrentCompanySettings()
    {
        var invoiceGateway = new RecordingAsaasInvoiceGateway(
            scheduleFailureMessage: "Conforme legislação municipal, deve ter retenção de ISS.");
        var context = await CreateApprovedDraftAsync(invoiceGateway: invoiceGateway);
        await ExecuteProductionBatchAsync(context);
        var failedInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));

        invoiceGateway.StopFailing();
        await context.CompanyRepository.UpsertAsync(
            ApplyIssRetention(await context.CompanyRepository.FindByTaxIdAsync(
                CompanyTaxIdValue,
                CancellationToken.None)),
            CancellationToken.None);

        var reissuedInvoice = await context.FiscalInvoiceService.ReissueAsync(
            failedInvoice.Id,
            new ReissueFiscalInvoiceRequest("CONFIRMAR"),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(2, reissuedInvoice.Sequence);
        Assert.Equal("Scheduled", reissuedInvoice.Status);
        Assert.True(Assert.Single(invoiceGateway.ScheduledRequests).RetainsIss);
    }

    /// <summary>
    /// Nota nº1 falha, é reemitida com sucesso como nº2. Acionar a nº1 de novo
    /// não pode emitir uma nº3: a prefeitura já tem uma nota viva (a nº2) para
    /// esta prévia, e uma segunda nota válida seria duplicação irreversível — o
    /// cancelamento já foi negado por competência encerrada em casos reais.
    /// </summary>
    [Fact]
    public async Task ReissueAsync_RefusesASecondReissueOfAnInvoiceAlreadySucceeded()
    {
        var invoiceGateway = new RecordingAsaasInvoiceGateway(
            scheduleFailureMessage: "Conforme legislação municipal, deve ter retenção de ISS.");
        var context = await CreateApprovedDraftAsync(invoiceGateway: invoiceGateway);
        await ExecuteProductionBatchAsync(context);
        var firstInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));

        invoiceGateway.StopFailing();
        await context.FiscalInvoiceService.ReissueAsync(
            firstInvoice.Id,
            new ReissueFiscalInvoiceRequest("CONFIRMAR"),
            OperatorId,
            CancellationToken.None);
        var scheduleCallCountAfterSuccessfulReissue = invoiceGateway.ScheduleCallCount;

        await Assert.ThrowsAsync<ConflictException>(() => context.FiscalInvoiceService.ReissueAsync(
            firstInvoice.Id,
            new ReissueFiscalInvoiceRequest("CONFIRMAR"),
            OperatorId,
            CancellationToken.None));
        Assert.Equal(scheduleCallCountAfterSuccessfulReissue, invoiceGateway.ScheduleCallCount);
    }

    private static Company ApplyIssRetention(Company? company)
    {
        ArgumentNullException.ThrowIfNull(company);
        company.SetIssRetention(true, OperatorId, DateTimeOffset.UtcNow);
        return company;
    }

    private static async Task<ChargeBatchResponse> ExecuteProductionBatchAsync(TestContext context)
    {
        var chargeBatch = await context.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                new DateOnly(2026, 9, 10),
                "Production",
                [context.BillingDraftId]),
            OperatorId,
            CancellationToken.None);
        await context.ChargeBatchService.ApproveAsync(
            chargeBatch.Id,
            OperatorId,
            CancellationToken.None);
        return await context.ChargeBatchService.ExecuteAsync(
            chargeBatch.Id,
            new ExecuteChargeBatchRequest("CONFIRMAR"),
            OperatorId,
            CancellationToken.None);
    }

    private static async Task<TestContext> CreateApprovedDraftAsync(
        bool retainsIss = false,
        AsaasOptions? asaasOptions = null,
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

        // Desde que a criacao da cobranca resolve o cliente Asaas pelo catalogo,
        // e nao pelo que a previa guardou, a empresa do cenario precisa ter o
        // vinculo nos dois ambientes.
        company.LinkAsaasCustomer(
            AsaasEnvironment.Sandbox, "cus_000123", OperatorId, DateTimeOffset.UtcNow);
        company.LinkAsaasCustomer(
            AsaasEnvironment.Production, "cus_000123", OperatorId, DateTimeOffset.UtcNow);
        await companyRepository.UpsertAsync(company, CancellationToken.None);

        var chargeCreationService = new ChargeCreationService(
            billingPeriodRepository,
            billingDraftRepository,
            companyRepository,
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
            Options.Create(asaasOptions ?? IssuingEnabledAsaasOptions()),
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
            chargeBatchRepository,
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
            companyRepository,
            billingDraft.Id,
            billingPeriod.Id);
    }


    /// <summary>
    /// Emissão habilitada nos dois ambientes. Os testes exercitam a regra do
    /// serviço, não a política de configuração — essa tem testes próprios em
    /// AsaasOperationPolicyTests.
    /// </summary>
    private static AsaasOptions IssuingEnabledAsaasOptions()
    {
        return new AsaasOptions
        {
            Sandbox = new AsaasConnectionOptions
            {
                BaseUrl = "https://api-sandbox.asaas.com/v3/",
                ApiKey = "chave-sandbox",
                AllowChargeCreation = true,
                AllowInvoiceIssuance = true,
            },
            Production = new AsaasConnectionOptions
            {
                BaseUrl = "https://api.asaas.com/v3/",
                ApiKey = "chave-producao",
                AllowChargeCreation = true,
                AllowInvoiceIssuance = true,
            },
        };
    }

    private sealed record TestContext(
        ChargeBatchService ChargeBatchService,
        FiscalInvoiceService FiscalInvoiceService,
        IFiscalInvoiceRepository FiscalInvoiceRepository,
        InMemoryAuditLogRepository AuditLogRepository,
        RecordingAsaasInvoiceGateway InvoiceGateway,
        InMemoryCompanyRepository CompanyRepository,
        Guid BillingDraftId,
        Guid BillingPeriodId);

    private sealed class RecordingAsaasInvoiceGateway(
        string? scheduleFailureMessage = null,
        string statusOnGet = "AUTHORIZED") : IAsaasInvoiceGateway
    {
        private readonly List<AsaasInvoiceRequest> scheduledRequests = [];
        private readonly List<AsaasEnvironment> scheduledEnvironments = [];
        private readonly List<string> queriedInvoiceIds = [];
        private string? currentFailureMessage = scheduleFailureMessage;
        private string? pdfUrlOnGet;
        private string? xmlUrlOnGet;

        public int ScheduleCallCount { get; private set; }

        public int GetCallCount { get; private set; }

        public IReadOnlyList<AsaasInvoiceRequest> ScheduledRequests => scheduledRequests;

        public IReadOnlyList<AsaasEnvironment> ScheduledEnvironments => scheduledEnvironments;

        public IReadOnlyList<string> QueriedInvoiceIds => queriedInvoiceIds;

        /// <summary>Simula a empresa corrigindo o cadastro depois da recusa da prefeitura.</summary>
        public void StopFailing() => currentFailureMessage = null;

        public void ReturnDocumentsOnGet(string? pdfUrl, string? xmlUrl)
        {
            pdfUrlOnGet = pdfUrl;
            xmlUrlOnGet = xmlUrl;
        }

        public Task<AsaasInvoiceCreation> ScheduleInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasInvoiceRequest request,
            CancellationToken cancellationToken)
        {
            ScheduleCallCount++;
            if (currentFailureMessage is not null)
            {
                throw new ExternalOperationNotAllowedException(currentFailureMessage);
            }

            scheduledRequests.Add(request);
            scheduledEnvironments.Add(asaasEnvironment);
            return Task.FromResult(new AsaasInvoiceCreation("inv_000022573933", "SCHEDULED"));
        }

        public Task<AsaasInvoiceState> GetInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasInvoiceId,
            CancellationToken cancellationToken)
        {
            GetCallCount++;
            queriedInvoiceIds.Add(asaasInvoiceId);
            return Task.FromResult(new AsaasInvoiceState(asaasInvoiceId, statusOnGet, null, pdfUrlOnGet, xmlUrlOnGet));
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

    /// <summary>
    /// Conta chamadas a <see cref="IFiscalInvoiceRepository.UpdateAsync"/> sem
    /// mudar o comportamento de persistência. É a forma correta de provar que
    /// <c>SynchronizeAsync</c> não gravou nada quando o status não muda: o
    /// objeto em memória é compartilhado por referência, então checar
    /// <c>UpdatedAt</c> no repositório InMemory não pega a ausência da gravação.
    /// </summary>
    private sealed class CountingFiscalInvoiceRepository(IFiscalInvoiceRepository innerRepository)
        : IFiscalInvoiceRepository
    {
        public int UpdateCallCount { get; private set; }

        public Task AddAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
        {
            return innerRepository.AddAsync(fiscalInvoice, cancellationToken);
        }

        public Task UpdateAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            return innerRepository.UpdateAsync(fiscalInvoice, cancellationToken);
        }

        public Task<FiscalInvoice?> FindByIdAsync(Guid fiscalInvoiceId, CancellationToken cancellationToken)
        {
            return innerRepository.FindByIdAsync(fiscalInvoiceId, cancellationToken);
        }

        public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingDraftIdAsync(
            Guid billingDraftId,
            CancellationToken cancellationToken)
        {
            return innerRepository.ListByBillingDraftIdAsync(billingDraftId, cancellationToken);
        }

        public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingPeriodIdAsync(
            Guid billingPeriodId,
            CancellationToken cancellationToken)
        {
            return innerRepository.ListByBillingPeriodIdAsync(billingPeriodId, cancellationToken);
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

        public Task<AsaasChargeState> GetChargeAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasPaymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasChargeState(asaasPaymentId, "PENDING", null));
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
