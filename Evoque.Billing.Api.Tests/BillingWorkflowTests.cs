using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;
using Microsoft.Extensions.Options;

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
                new DateOnly(2026, 8, 10),
                "CONFIRMAR",
                [firstBillingDraft.Id, secondBillingDraft.Id]),
            "maria",
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
                new DateOnly(2026, 8, 20),
                "Sandbox",
                [billingDraft.Id]),
            "maria",
            CancellationToken.None);

        Assert.Equal("AwaitingApproval", preview.Status);
        Assert.Equal("Sandbox", preview.AsaasEnvironment);
        Assert.False(asaasChargeGateway.WasCalled);

        var approvedBatch = await services.ChargeBatchService.ApproveAsync(
            preview.Id,
            "maria",
            CancellationToken.None);
        Assert.Equal("Approved", approvedBatch.Status);
        Assert.Equal("maria", approvedBatch.ApprovedBy);
        Assert.False(asaasChargeGateway.WasCalled);

        var completedBatch = await services.ChargeBatchService.ExecuteAsync(
            preview.Id,
            new ExecuteChargeBatchRequest("CONFIRMAR"),
            "maria",
            CancellationToken.None);

        Assert.Equal("Completed", completedBatch.Status);
        Assert.Equal(1, asaasChargeGateway.CallCount);

        var billingDraftAfterSandboxExecution = await services.BillingDraftService.GetByIdAsync(
            billingDraft.Id,
            CancellationToken.None);
        Assert.Equal(BillingDraftStatus.Approved, billingDraftAfterSandboxExecution.Status);

        var productionPreview = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                new DateOnly(2026, 8, 20),
                "Production",
                [billingDraft.Id]),
            "maria",
            CancellationToken.None);
        await services.ChargeBatchService.ApproveAsync(
            productionPreview.Id,
            "maria",
            CancellationToken.None);
        var productionBatch = await services.ChargeBatchService.ExecuteAsync(
            productionPreview.Id,
            new ExecuteChargeBatchRequest("CONFIRMAR"),
            "maria",
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
                    new DateOnly(2020, 1, 2),
                    "Sandbox",
                    [billingDraft.Id]),
                "maria",
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
            new DateOnly(2026, 9, 5),
            "Sandbox",
            [billingDraft.Id]);
        var firstChargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            firstRequest,
            "maria",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            services.ChargeBatchService.CreatePreviewAsync(
                new CreateChargeBatchPreviewRequest(
                    new DateOnly(2026, 9, 5),
                    "Sandbox",
                    [billingDraft.Id]),
                "maria",
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
            new CreateChargeBatchPreviewRequest(new DateOnly(2026, 9, 5), "Sandbox", [billingDraft.Id]),
            "maria",
            CancellationToken.None);
        await services.ChargeBatchService.ApproveAsync(
            firstChargeBatch.Id,
            "maria",
            CancellationToken.None);
        await services.ChargeBatchService.ExecuteAsync(
            firstChargeBatch.Id,
            new ExecuteChargeBatchRequest("CONFIRMAR"),
            "maria",
            CancellationToken.None);

        var secondChargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(new DateOnly(2026, 9, 5), "Sandbox", [billingDraft.Id]),
            "maria",
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
            new UpsertCompanyBillingScheduleRequest(20, true),
            "maria",
            CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-fechamento-02",
            new UpsertCompanyBillingScheduleRequest(2, true),
            "maria",
            CancellationToken.None);

        var preview = await services.ScheduledChargeBatchService.CreatePreviewAsync(
            billingPeriodReference,
            new CreateScheduledChargeBatchPreviewRequest(
                20,
                new DateOnly(2026, 9, 2),
                "Sandbox"),
            "maria",
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
                    25,
                    new DateOnly(2026, 8, 10),
                    "Sandbox"),
                "maria",
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
            new UpsertCompanyBillingScheduleRequest(20, true),
            "maria",
            CancellationToken.None);
        await services.CompanyBillingScheduleService.UpsertAsync(
            "empresa-dia-02",
            new UpsertCompanyBillingScheduleRequest(2, true),
            "maria",
            CancellationToken.None);

        // Fechamento no dia 20 de agosto, vencimento em 5 de setembro: é assim
        // que as cobranças reais aparecem no Asaas.
        var preview = await services.ScheduledChargeBatchService.CreatePreviewAsync(
            billingPeriodReference,
            new CreateScheduledChargeBatchPreviewRequest(
                20,
                new DateOnly(2026, 9, 5),
                "Sandbox"),
            "maria",
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
            new UpsertCompanyBillingScheduleRequest(20, true),
            "maria",
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
                    20,
                    new DateOnly(2026, 9, 5),
                    "Sandbox"),
                "maria",
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
                new DateOnly(2026, 8, 10),
                "CONFIRMAR",
                [firstBillingDraft.Id, secondBillingDraft.Id]),
            "maria",
            CancellationToken.None);

        Assert.Equal("CompletedWithErrors", originalBatch.Status);
        Assert.Single(originalBatch.Items, item => item.Status == "Failed");

        var retryBatch = await services.ChargeBatchService.RetryFailedAsync(
            originalBatch.Id,
            new RetryFailedChargeBatchRequest("CONFIRMAR"),
            "maria",
            CancellationToken.None);

        Assert.Equal(originalBatch.Id, retryBatch.RetryOfChargeBatchId);
        Assert.Single(retryBatch.Items);
        Assert.Equal("Created", retryBatch.Items.Single().Status);
        Assert.Equal(3, asaasChargeGateway.CallCount);
    }

    /// <summary>
    /// A prévia gerada a partir do catálogo nasce sem identificador de cliente
    /// Asaas, de propósito: ele pertence a um ambiente, e a prévia não sabe em
    /// qual lote vai ser executada. Quem resolve é a execução, consultando o
    /// catálogo no ambiente do lote.
    ///
    /// Sem isto, nenhuma das prévias geradas do catálogo viraria cobrança.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ResolvesTheAsaasCustomerFromTheCatalogue()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        const string companyTaxId = "56087276000103";

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var company = Company.CreateManually(companyTaxId, "Open Sports", "maria", DateTimeOffset.UtcNow);
        company.LinkAsaasCustomer(
            AsaasEnvironment.Sandbox, "cus_sandbox_open", "maria", DateTimeOffset.UtcNow);
        await services.CompanyRepository.UpsertAsync(company, CancellationToken.None);

        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            new CreateBillingDraftCommand(
                companyTaxId,
                "Open Sports",
                companyTaxId,
                AsaasCustomerId: null,
                [new CreateBillingDraftItemCommand("Plano corporativo", 1, 89.90m, "member-1")]),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var resultado = await services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "CONFIRMAR",
            AsaasEnvironment.Sandbox,
            CancellationToken.None);

        Assert.NotNull(resultado.AsaasPaymentId);
        Assert.Equal("cus_sandbox_open", asaasChargeGateway.LastCustomerId);
    }

    /// <summary>
    /// O bug que a pendência 1.1 descreve: a prévia importada guardava o cliente
    /// do Sandbox, e um lote de Produção o enviaria para a conta real, onde ele
    /// não existe. O ambiente do lote é quem decide.
    /// </summary>
    [Fact]
    public async Task CreateAsync_UsesTheCustomerOfTheEnvironmentTheBatchRunsIn()
    {
        var asaasChargeGateway = new RecordingAsaasChargeGateway();
        var services = CreateServices(asaasChargeGateway);
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        const string companyTaxId = "56087276000103";

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var company = Company.CreateManually(companyTaxId, "Open Sports", "maria", DateTimeOffset.UtcNow);
        company.LinkAsaasCustomer(
            AsaasEnvironment.Sandbox, "cus_sandbox_open", "maria", DateTimeOffset.UtcNow);
        company.LinkAsaasCustomer(
            AsaasEnvironment.Production, "cus_producao_open", "maria", DateTimeOffset.UtcNow);
        await services.CompanyRepository.UpsertAsync(company, CancellationToken.None);

        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            new CreateBillingDraftCommand(
                companyTaxId,
                "Open Sports",
                companyTaxId,
                AsaasCustomerId: "cus_sandbox_open",
                [new CreateBillingDraftItemCommand("Plano corporativo", 1, 89.90m, "member-1")]),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        await services.ChargeCreationService.CreateAsync(
            billingDraft.Id,
            new DateOnly(2026, 8, 10),
            "maria",
            "CONFIRMAR",
            AsaasEnvironment.Production,
            CancellationToken.None);

        Assert.Equal("cus_producao_open", asaasChargeGateway.LastCustomerId);
    }

    /// <summary>
    /// A empresa existe e tem preço, mas ninguém sincronizou o cliente espelho
    /// naquele ambiente. A mensagem precisa dizer isso, e não "não encontrado".
    /// </summary>
    [Fact]
    public async Task CreateAsync_RefusesWhenTheCompanyHasNoCustomerInThatEnvironment()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        const string companyTaxId = "56087276000103";

        await services.BillingPeriodService.CreateAsync(billingPeriodReference, "maria", CancellationToken.None);
        var company = Company.CreateManually(companyTaxId, "Open Sports", "maria", DateTimeOffset.UtcNow);
        await services.CompanyRepository.UpsertAsync(company, CancellationToken.None);

        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            new CreateBillingDraftCommand(
                companyTaxId,
                "Open Sports",
                companyTaxId,
                AsaasCustomerId: null,
                [new CreateBillingDraftItemCommand("Plano corporativo", 1, 89.90m, "member-1")]),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(billingDraft.Id, "maria", CancellationToken.None);

        var excecao = await Assert.ThrowsAsync<ValidationException>(() =>
            services.ChargeCreationService.CreateAsync(
                billingDraft.Id,
                new DateOnly(2026, 8, 10),
                "maria",
                "CONFIRMAR",
                AsaasEnvironment.Sandbox,
                CancellationToken.None));
        Assert.Contains("Sandbox", excecao.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsync_PreservesAuditFieldsAndAllowsANewVersion()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        await services.BillingPeriodService.CreateAsync(
            billingPeriodReference,
            "maria",
            CancellationToken.None);
        var originalBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);

        var cancelledBillingDraft = await services.BillingDraftService.CancelAsync(
            originalBillingDraft.Id,
            "Roster da competência foi atualizado.",
            "geovanna",
            CancellationToken.None);
        var replacementBillingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "geovanna",
            CancellationToken.None);

        Assert.Equal(BillingDraftStatus.Cancelled, cancelledBillingDraft.Status);
        Assert.Equal("geovanna", cancelledBillingDraft.CancelledBy);
        Assert.NotNull(cancelledBillingDraft.CancelledAt);
        Assert.Equal("Roster da competência foi atualizado.", cancelledBillingDraft.CancellationReason);
        Assert.Equal(1, cancelledBillingDraft.Version);
        Assert.Equal(2, replacementBillingDraft.Version);
        Assert.Equal(BillingDraftStatus.PendingReview, replacementBillingDraft.Status);
    }

    [Fact]
    public async Task CancelAsync_RefusesDraftWithASandboxChargeAlreadyCreated()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        await services.BillingPeriodService.CreateAsync(
            billingPeriodReference,
            "maria",
            CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(
            billingDraft.Id,
            "maria",
            CancellationToken.None);
        var chargeBatch = await services.ChargeBatchService.CreateAsync(
            new CreateChargeBatchRequest(
                new DateOnly(2026, 8, 20),
                "CONFIRMAR",
                [billingDraft.Id]),
            "maria",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            services.BillingDraftService.CancelAsync(
                billingDraft.Id,
                "Tentativa indevida.",
                "geovanna",
                CancellationToken.None));

        Assert.Equal("Completed", chargeBatch.Status);
        Assert.Contains("cobrança criada no Asaas", exception.Message);
    }

    [Fact]
    public async Task ApproveBatchAsync_RefusesBatchContainingACancelledDraft()
    {
        var services = CreateServices();
        var billingPeriodReference = new BillingPeriodReference(2026, 8);
        await services.BillingPeriodService.CreateAsync(
            billingPeriodReference,
            "maria",
            CancellationToken.None);
        var billingDraft = await services.BillingDraftService.CreateAsync(
            billingPeriodReference,
            CreateDraftCommand("empresa-1", "Empresa Um"),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.ApproveAsync(
            billingDraft.Id,
            "maria",
            CancellationToken.None);
        var chargeBatch = await services.ChargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                new DateOnly(2026, 8, 20),
                "Sandbox",
                [billingDraft.Id]),
            "maria",
            CancellationToken.None);
        await services.BillingDraftService.CancelAsync(
            billingDraft.Id,
            "Roster da competência foi atualizado.",
            "geovanna",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            services.ChargeBatchService.ApproveAsync(
                chargeBatch.Id,
                "geovanna",
                CancellationToken.None));

        Assert.Contains("prévia cancelada", exception.Message);
    }

    private static CreateBillingDraftCommand CreateDraftCommand(string externalCompanyId, string companyName)
    {
        return new CreateBillingDraftCommand(
            externalCompanyId,
            companyName,
            "02346076000107",
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

        // A previa sempre pertence a uma empresa do catalogo: a importacao
        // vincula pelo CNPJ. Desde que a criacao da cobranca resolve o cliente
        // Asaas pelo catalogo, e nao pelo que a previa guardou, os cenarios
        // precisam dessa empresa existir com cliente nos dois ambientes.
        var defaultCompany = Company.CreateManually(
            "02346076000107", "Empresa de teste", "maria", DateTimeOffset.UtcNow);
        defaultCompany.LinkAsaasCustomer(
            AsaasEnvironment.Sandbox, "cus_000123", "maria", DateTimeOffset.UtcNow);
        defaultCompany.LinkAsaasCustomer(
            AsaasEnvironment.Production, "cus_000123", "maria", DateTimeOffset.UtcNow);
        dataStore.Companies[defaultCompany.TaxId] = defaultCompany;

        // Relógio fixo: os vencimentos dos cenários são datas fixas de 2026, e um
        // TimeProvider real faria a suíte quebrar sozinha assim que o calendário
        // ultrapassasse essas datas.
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var chargeCreationService = new ChargeCreationService(
            billingPeriodRepository,
            billingDraftRepository,
            companyRepository,
            auditLogRepository,
            notificationGateway ?? new RecordingAsaasCustomerNotificationGateway(
                new AsaasCustomerEmailDeliveryReadiness(true, true)),
            asaasChargeGateway ?? new RecordingAsaasChargeGateway(),
            timeProvider);
        var fiscalInvoiceService = new FiscalInvoiceService(
            billingPeriodRepository,
            billingDraftRepository,
            new InMemoryFiscalInvoiceRepository(dataStore),
            companyRepository,
            auditLogRepository,
            new NoopAsaasInvoiceGateway(),
            Options.Create(new FiscalInvoiceOptions
            {
                MunicipalServiceId = "82367",
                IssTaxRate = 5.00m,
            }),
            // Emissão desligada: estes testes são do fluxo de cobrança, e uma
            // nota tentando sair a cada lote só acrescentaria ruído.
            Options.Create(new AsaasOptions()),
            timeProvider);

        var chargeBatchService = new ChargeBatchService(
            billingPeriodRepository,
            billingDraftRepository,
            chargeBatchRepository,
            auditLogRepository,
            chargeCreationService,
            fiscalInvoiceService,
            timeProvider);
        var companyBillingScheduleService = new CompanyBillingScheduleService(
            companyBillingScheduleRepository,
            auditLogRepository);

        return new TestServices(
            new BillingPeriodService(billingPeriodRepository, auditLogRepository),
            new BillingDraftService(
                billingPeriodRepository,
                billingDraftRepository,
                chargeBatchRepository,
                auditLogRepository),
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

        /// <summary>
        /// O cliente Asaas que a ultima cobranca recebeu. E o que prova de onde
        /// o identificador veio: da previa ou do catalogo, e de qual ambiente.
        /// </summary>
        public string? LastCustomerId { get; private set; }

        public Task<AsaasChargeCreation> CreateChargeAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasChargeRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCustomerId = request.CustomerId;
            if (CallCount == failOnCall)
            {
                throw new ExternalOperationNotAllowedException("Falha simulada do Asaas.");
            }

            return Task.FromResult(new AsaasChargeCreation(
                "pay_000123",
                "https://sandbox.asaas.com/pdf/pay_000123"));
        }

        public Task<AsaasChargeState> GetChargeAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasPaymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasChargeState(asaasPaymentId, "PENDING", null));
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

    private sealed class NoopAsaasInvoiceGateway : IAsaasInvoiceGateway
    {
        public Task<AsaasInvoiceCreation> ScheduleInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasInvoiceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasInvoiceCreation("inv_000000000001", "SCHEDULED"));
        }

        public Task<AsaasInvoiceState> GetInvoiceAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasInvoiceId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasInvoiceState(asaasInvoiceId, "AUTHORIZED", null, null, null));
        }
    }
}
