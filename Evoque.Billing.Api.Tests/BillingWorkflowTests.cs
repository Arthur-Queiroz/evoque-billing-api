using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class BillingWorkflowTests
{
    [Fact]
    public void Compare_ReturnsOnlyChangesBetweenMonthlySnapshots()
    {
        var service = new MonthlyComparisonService();

        var comparisonResults = service.Compare(
            [new CompanyBillingSnapshot(
                "empresa-1",
                "Empresa Um",
                [
                    new MemberBillingSnapshot("membro-1", "Ana", 79.90m, true),
                    new MemberBillingSnapshot("membro-2", "Bruno", 49.90m, true),
                ])],
            [new CompanyBillingSnapshot(
                "empresa-1",
                "Empresa Um",
                [
                    new MemberBillingSnapshot("membro-1", "Ana", 89.90m, true),
                    new MemberBillingSnapshot("membro-2", "Bruno", 49.90m, false),
                    new MemberBillingSnapshot("membro-3", "Carla", 79.90m, true),
                ])]);

        var comparisonResult = Assert.Single(comparisonResults);
        Assert.Equal(129.80m, comparisonResult.PreviousTotalAmount);
        Assert.Equal(169.80m, comparisonResult.CurrentTotalAmount);
        Assert.Collection(
            comparisonResult.Changes,
            change => Assert.Equal(MemberComparisonType.AmountChanged, change.Type),
            change => Assert.Equal(MemberComparisonType.Deactivated, change.Type),
            change => Assert.Equal(MemberComparisonType.Added, change.Type));
    }

    [Fact]
    public async Task ApproveAsync_ApprovesBillingPeriodWhenEveryDraftIsApproved()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var firstBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Evoque Empresa Um"),
            "maria",
            CancellationToken.None);
        var secondBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-2", "Evoque Empresa Dois"),
            "maria",
            CancellationToken.None);

        await services.BillingDraftService.ApproveAsync(firstBillingDraft.Id, "maria", CancellationToken.None);

        var billingPeriodAfterFirstApproval = await services.BillingPeriodService.GetByReferenceAsync(
            billingPeriodReference,
            CancellationToken.None);
        Assert.Equal(BillingPeriodStatus.AwaitingReview, billingPeriodAfterFirstApproval.Status);

        await services.BillingDraftService.ApproveAsync(secondBillingDraft.Id, "maria", CancellationToken.None);

        var billingPeriodAfterSecondApproval = await services.BillingPeriodService.GetByReferenceAsync(
            billingPeriodReference,
            CancellationToken.None);
        Assert.Equal(BillingPeriodStatus.Approved, billingPeriodAfterSecondApproval.Status);
    }

    [Fact]
    public async Task CreateAsync_AllowsAddingAnotherCompanyToAnApprovedOpenPeriod()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var firstBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Evoque Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(firstBillingDraft.Id, "maria", CancellationToken.None);

        var secondBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-2", "Evoque Empresa Dois"),
            "maria",
            CancellationToken.None);

        var billingPeriodAfterSecondDraft = await services.BillingPeriodService.GetByReferenceAsync(
            billingPeriodReference,
            CancellationToken.None);
        Assert.Equal(BillingDraftStatus.PendingReview, secondBillingDraft.Status);
        Assert.Equal(BillingPeriodStatus.AwaitingReview, billingPeriodAfterSecondDraft.Status);

        await services.BillingDraftService.ApproveAsync(secondBillingDraft.Id, "maria", CancellationToken.None);

        var billingPeriodAfterSecondApproval = await services.BillingPeriodService.GetByReferenceAsync(
            billingPeriodReference,
            CancellationToken.None);
        Assert.Equal(BillingPeriodStatus.Approved, billingPeriodAfterSecondApproval.Status);
    }

    [Fact]
    public async Task CreateAsync_DoesNotCallAsaasWithoutExplicitOperatorConfirmation()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Evoque Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "",
            AsaasEnvironment.Sandbox,
            CancellationToken.None));

        Assert.False(asaasChargeGateway.WasCalled);
    }

    [Fact]
    public async Task CreateAsync_IsIdempotentAfterChargeWasCreated()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Evoque Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var firstResult = await services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "CONFIRMAR",
            AsaasEnvironment.Production,
            CancellationToken.None);
        var secondResult = await services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "CONFIRMAR",
            AsaasEnvironment.Production,
            CancellationToken.None);

        Assert.True(firstResult.CreatedNow);
        Assert.False(secondResult.CreatedNow);
        Assert.Equal(firstResult.AsaasPaymentId, secondResult.AsaasPaymentId);
        Assert.Equal(1, asaasChargeGateway.CallCount);
    }

    [Fact]
    public async Task CreateAsync_DoesNotCreateChargeWhenAsaasEmailNotificationIsDisabled()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var notificationGateway = new RecordingAsaasCustomerNotificationGateway(
            new AsaasCustomerEmailDeliveryReadiness(true, false));
        var services = CreateServices(asaasChargeGateway, notificationGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Evoque Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "CONFIRMAR",
            AsaasEnvironment.Sandbox,
            CancellationToken.None));

        Assert.False(asaasChargeGateway.WasCalled);
    }

    [Fact]
    public async Task CreateAsync_CreatesEveryEligibleDraftInBatchAfterTextConfirmation()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var firstBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        var secondBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-2", "Empresa Dois"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(firstBillingDraft.Id, "maria", CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(secondBillingDraft.Id, "maria", CancellationToken.None);

        var result = await services.ChargeBatchService.CreateAsync(
            new CreateChargeBatchRequest(
                "maria",
                new DateOnly(2026, 8, 10),
                "CONFIRMAR",
                [firstBillingDraft.Id, secondBillingDraft.Id]),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.True(item.Created));
        Assert.Equal(2, asaasChargeGateway.CallCount);
    }

    [Fact]
    public async Task PreviewApproveAndExecuteAsync_CallsAsaasOnlyAfterApprovalAndConfirmation()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var preview = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                "maria",
                new DateOnly(2026, 8, 20),
                "Sandbox",
                [billingDraft.Id]),
            CancellationToken.None);

        Assert.Equal("AwaitingApproval", preview.Status);
        Assert.Equal("Sandbox", preview.AsaasEnvironment);
        Assert.False(asaasChargeGateway.WasCalled);

        var approvedBatch = await services.ChargeBatchService.ApproveAsync(
            preview.Id,
            new ApproveChargeBatchRequest("maria"),
            CancellationToken.None);
        Assert.Equal("Approved", approvedBatch.Status);
        Assert.Equal("maria", approvedBatch.ApprovedBy);
        Assert.False(asaasChargeGateway.WasCalled);

        var completedBatch = await services.ChargeBatchService.ExecuteAsync(
            preview.Id,
            new ExecuteChargeBatchRequest("maria", "CONFIRMAR"),
            CancellationToken.None);

        Assert.Equal("Completed", completedBatch.Status);
        Assert.Equal(1, asaasChargeGateway.CallCount);

        var billingDraftAfterSandboxExecution = await services.BillingDraftService.GetByIdAsync(
            billingDraft.Id,
            CancellationToken.None);
        Assert.Equal(BillingDraftStatus.Approved, billingDraftAfterSandboxExecution.Status);

        var productionPreview = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                "maria",
                new DateOnly(2026, 8, 20),
                "Production",
                [billingDraft.Id]),
            CancellationToken.None);
        await services.ChargeBatchService.ApproveAsync(
            productionPreview.Id,
            new ApproveChargeBatchRequest("maria"),
            CancellationToken.None);
        var productionBatch = await services.ChargeBatchService.ExecuteAsync(
            productionPreview.Id,
            new ExecuteChargeBatchRequest("maria", "CONFIRMAR"),
            CancellationToken.None);

        Assert.Equal("Completed", productionBatch.Status);
        Assert.Equal(2, asaasChargeGateway.CallCount);
    }

    [Fact]
    public async Task CreatePreviewAsync_RejectsPastDueDateBeforeCallingAsaas()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            services.ChargeBatchService.CreatePreviewAsync(
                new CreateChargeBatchPreviewRequest(
                    "maria",
                    new DateOnly(2020, 1, 2),
                    "Sandbox",
                    [billingDraft.Id]),
                CancellationToken.None));

        Assert.Contains("já passou", exception.Message);
        Assert.False(asaasChargeGateway.WasCalled);
    }

    /// <summary>
    /// Clicar duas vezes em "Gerar prévia do ciclo" acumulava lotes idênticos.
    /// Cada um deles emitiria uma cobrança para a mesma prévia, e no Sandbox a
    /// idempotência não protege: sairiam boletos duplicados para o cliente.
    /// </summary>
    [Fact]
    public async Task CreatePreviewAsync_RefusesADraftThatIsAlreadyInAnUnresolvedBatch()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-unica", "Empresa Única"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var firstRequest = new CreateChargeBatchPreviewRequest(
            "maria",
            new DateOnly(2026, 9, 5),
            "Sandbox",
            [billingDraft.Id]);
        var firstChargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            firstRequest,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            services.ChargeBatchService.CreatePreviewAsync(
                new CreateChargeBatchPreviewRequest(
                    "maria",
                    new DateOnly(2026, 9, 5),
                    "Sandbox",
                    [billingDraft.Id]),
                CancellationToken.None));

        Assert.Contains(firstChargeBatch.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task CreatePreviewAsync_AllowsANewBatchAfterThePreviousOneIsResolved()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-unica", "Empresa Única"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);
        var firstChargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest("maria", new DateOnly(2026, 9, 5), "Sandbox", [billingDraft.Id]),
            CancellationToken.None);
        await services.ChargeBatchService.ApproveAsync(
            firstChargeBatch.Id,
            new ApproveChargeBatchRequest("maria"),
            CancellationToken.None);
        await services.ChargeBatchService.ExecuteAsync(
            firstChargeBatch.Id,
            new ExecuteChargeBatchRequest("maria", "CONFIRMAR"),
            CancellationToken.None);

        var secondChargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest("maria", new DateOnly(2026, 9, 5), "Sandbox", [billingDraft.Id]),
            CancellationToken.None);

        Assert.NotEqual(firstChargeBatch.Id, secondChargeBatch.Id);
    }

    /// <summary>
    /// Regressão do motivo pelo qual o lote agendado nunca achava empresa: ele
    /// filtrava pelo dia do vencimento. Nenhum vencimento real cai em 02, 18, 20
    /// ou 25 — eles caem em 06, 10, 12, 27, 30 — então o filtro voltava vazio.
    /// Aqui o vencimento cai no dia 02 e a empresa selecionada é a do
    /// fechamento 20, não a do fechamento 02.
    /// </summary>
    [Fact]
    public async Task ScheduledPreviewAsync_SelectsByClosingDayNotByTheDueDateDay()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var closingDayTwentyDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-fechamento-20", "Empresa Fechamento 20"),
            "maria",
            CancellationToken.None);
        var closingDayTwoDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-fechamento-02", "Empresa Fechamento 02"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(closingDayTwentyDraft.Id, "maria", CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(closingDayTwoDraft.Id, "maria", CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-fechamento-20",
            new UpsertCompanyBillingScheduleRequest(20, true, "maria"),
            CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-fechamento-02",
            new UpsertCompanyBillingScheduleRequest(2, true, "maria"),
            CancellationToken.None);

        var preview = await services.ScheduledChargeBatchService.CreatePreviewAsync(
            billingPeriodReference,
            new CreateScheduledChargeBatchPreviewRequest(
                "maria",
                20,
                new DateOnly(2026, 9, 2),
                "Sandbox"),
            CancellationToken.None);

        Assert.Equal(closingDayTwentyDraft.Id, Assert.Single(preview.Items).BillingDraftId);
    }

    [Fact]
    public async Task ScheduledPreviewAsync_RejectsADueDateBeforeThePeriodCloses()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            services.ScheduledChargeBatchService.CreatePreviewAsync(
                billingPeriodReference,
                new CreateScheduledChargeBatchPreviewRequest(
                    "maria",
                    25,
                    new DateOnly(2026, 8, 10),
                    "Sandbox"),
                CancellationToken.None));

        Assert.Contains("anterior ao fechamento", exception.Message);
    }

    [Fact]
    public async Task ScheduledPreviewAsync_UsesOnlyApprovedDraftsForCompaniesScheduledOnTheDueDay()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var scheduledDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-dia-20", "Empresa Dia 20"),
            "maria",
            CancellationToken.None);
        var unscheduledDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-dia-02", "Empresa Dia 02"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(scheduledDraft.Id, "maria", CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(unscheduledDraft.Id, "maria", CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-dia-20",
            new UpsertCompanyBillingScheduleRequest(20, true, "maria"),
            CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-dia-02",
            new UpsertCompanyBillingScheduleRequest(2, true, "maria"),
            CancellationToken.None);

        // Fechamento no dia 20 de agosto, vencimento em 5 de setembro: é assim
        // que as cobranças reais aparecem no Asaas.
        var preview = await services.ScheduledChargeBatchService.CreatePreviewAsync(
            billingPeriodReference,
            new CreateScheduledChargeBatchPreviewRequest(
                "maria",
                20,
                new DateOnly(2026, 9, 5),
                "Sandbox"),
            CancellationToken.None);

        var item = Assert.Single(preview.Items);
        Assert.Equal(scheduledDraft.Id, item.BillingDraftId);
        Assert.False(asaasChargeGateway.WasCalled);
    }

    [Fact]
    public async Task ScheduledPreviewAsync_ExcludesCompaniesDeactivatedInTheCatalog()
    {
        const string openSportsTaxId = "56087276000103";
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand(openSportsTaxId, "Open Sports"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            openSportsTaxId,
            new UpsertCompanyBillingScheduleRequest(20, true, "maria"),
            CancellationToken.None);

        // A empresa é inativada no catálogo, mas a agenda ativa permanece.
        var company = Company.CreateManually(
            openSportsTaxId,
            "Open Sports",
            "maria",
            DateTimeOffset.UtcNow);
        company.Deactivate("maria", DateTimeOffset.UtcNow);
        await services.CompanyRepository.UpsertAsync(company, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            services.ScheduledChargeBatchService.CreatePreviewAsync(
                billingPeriodReference,
                new CreateScheduledChargeBatchPreviewRequest(
                    "maria",
                    20,
                    new DateOnly(2026, 9, 5),
                    "Sandbox"),
                CancellationToken.None));
    }

    [Fact]
    public async Task RetryFailedAsync_CreatesOnlyTheItemsThatFailedInTheOriginalBatch()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway(failOnCall: 2);
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var firstBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        var secondBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-2", "Empresa Dois"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(firstBillingDraft.Id, "maria", CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(secondBillingDraft.Id, "maria", CancellationToken.None);

        var originalBatch = await services.ChargeBatchService.CreateAsync(
            new CreateChargeBatchRequest(
                "maria",
                new DateOnly(2026, 8, 10),
                "CONFIRMAR",
                [firstBillingDraft.Id, secondBillingDraft.Id]),
            CancellationToken.None);

        Assert.Equal("CompletedWithErrors", originalBatch.Status);
        Assert.Single(originalBatch.Items, item => item.Status == "Failed");

        var retryBatch = await services.ChargeBatchService.RetryFailedAsync(
            originalBatch.Id,
            new RetryFailedChargeBatchRequest("maria", "CONFIRMAR"),
            CancellationToken.None);

        Assert.Equal(originalBatch.Id, retryBatch.RetryOfChargeBatchId);
        Assert.Single(retryBatch.Items);
        Assert.Equal("Created", retryBatch.Items.Single().Status);
        Assert.Equal(3, asaasChargeGateway.CallCount);
    }

    private static CreateBillingDraftCommand CreateDraftCommand(string externalCompanyId, string companyName)
    {
        return new CreateBillingDraftCommand(
            externalCompanyId,
            companyName,
            "12345678000199",
            "cus_000123",
            [
                new CreateBillingDraftItemCommand("Plano corporativo", 2, 79.90m, "member-1"),
                new CreateBillingDraftItemCommand("Dependente", 1, 49.90m, "member-2"),
            ]);
    }

    private static TestServices CreateServices(
        IAsaasChargeGateway? asaasChargeGateway = null,
        IAsaasCustomerNotificationGateway? notificationGateway = null)
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var auditLogRepository = new InMemoryAuditLogRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);
        var companyBillingScheduleRepository = new InMemoryCompanyBillingScheduleRepository(dataStore);
        var companyRepository = new InMemoryCompanyRepository(dataStore);
        var chargeCreationService = new ChargeCreationService(
            billingPeriodRepository,
            billingDraftRepository,
            auditLogRepository,
            notificationGateway ?? new RecordingAsaasCustomerNotificationGateway(
                new AsaasCustomerEmailDeliveryReadiness(true, true)),
            asaasChargeGateway ?? new RecordingAsaasChargeGateway(),
            TimeProvider.System);

        var chargeBatchService = new ChargeBatchService(
            billingPeriodRepository,
            billingDraftRepository,
            chargeBatchRepository,
            auditLogRepository,
            chargeCreationService,
            TimeProvider.System);
        var companyBillingScheduleService = new CompanyBillingScheduleService(
            companyBillingScheduleRepository,
            auditLogRepository);

        return new TestServices(
            new BillingPeriodService(billingPeriodRepository, auditLogRepository),
            new BillingDraftService(billingPeriodRepository, billingDraftRepository, auditLogRepository),
            chargeCreationService,
            chargeBatchService,
            companyBillingScheduleService,
            new ScheduledChargeBatchService(
                billingPeriodRepository,
                billingDraftRepository,
                companyBillingScheduleRepository,
                companyRepository,
                chargeBatchService),
            companyRepository);
    }

    private sealed record TestServices(
        BillingPeriodService BillingPeriodService,
        BillingDraftService BillingDraftService,
        ChargeCreationService ChargeCreationService,
        ChargeBatchService ChargeBatchService,
        CompanyBillingScheduleService CompanyBillingScheduleService,
        ScheduledChargeBatchService ScheduledChargeBatchService,
        InMemoryCompanyRepository CompanyRepository);

    private sealed class RecordingAsaasChargeGateway(int? failOnCall = null) : IAsaasChargeGateway
    {
        public int CallCount { get; private set; }

        public bool WasCalled => CallCount > 0;

        public Task<AsaasChargeCreation> CreateChargeAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasChargeRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == failOnCall)
            {
                throw new ExternalOperationNotAllowedException("Falha simulada do Asaas.");
            }

            return Task.FromResult(new AsaasChargeCreation(
                "pay_000123",
                "https://sandbox.asaas.com/pdf/pay_000123"));
        }
    }

    private sealed class RecordingAsaasCustomerNotificationGateway(
        AsaasCustomerEmailDeliveryReadiness readiness) : IAsaasCustomerNotificationGateway
    {
        public Task<AsaasCustomerEmailDeliveryReadiness> GetEmailDeliveryReadinessAsync(
            AsaasEnvironment asaasEnvironment,
            string customerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(readiness);
        }
    }
}
