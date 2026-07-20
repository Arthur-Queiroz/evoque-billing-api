using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class ChargeBatchService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    IChargeBatchRepository chargeBatchRepository,
    IAuditLogRepository auditLogRepository,
    ChargeCreationService chargeCreationService)
{
    public async Task<ChargeBatchResponse> CreatePreviewAsync(
        CreateChargeBatchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var asaasEnvironment = ParseAsaasEnvironment(request.AsaasEnvironment);
        var billingDraftIds = NormalizeBillingDraftIds(request.BillingDraftIds);
        var chargeBatch = await CreatePreviewForBillingDraftsAsync(
            request.OperatorId,
            request.DueDate,
            asaasEnvironment,
            billingDraftIds,
            null,
            cancellationToken);

        return ChargeBatchResponse.FromDomain(chargeBatch);
    }

    // Mantido temporariamente para clientes que já usam a rota original.
    // Novas telas devem usar prévia, aprovação e execução em etapas separadas.
    public async Task<ChargeBatchResponse> CreateAsync(
        CreateChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfirmationPhrase(request.ConfirmationPhrase);
        var billingDraftIds = NormalizeBillingDraftIds(request.BillingDraftIds);
        var chargeBatch = await CreatePreviewForBillingDraftsAsync(
            request.OperatorId,
            request.DueDate,
            AsaasEnvironment.Sandbox,
            billingDraftIds,
            null,
            cancellationToken);

        await ApproveAsync(chargeBatch.Id, new ApproveChargeBatchRequest(request.OperatorId), cancellationToken);
        return await ExecuteAsync(
            chargeBatch.Id,
            new ExecuteChargeBatchRequest(request.OperatorId, request.ConfirmationPhrase),
            cancellationToken);
    }

    public async Task<ChargeBatchResponse> ApproveAsync(
        Guid chargeBatchId,
        ApproveChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await FindChargeBatchAsync(chargeBatchId, cancellationToken);
        var approvedAt = DateTimeOffset.UtcNow;
        chargeBatch.Approve(request.OperatorId, approvedAt);
        await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "charge-batch.approved",
                request.OperatorId,
                approvedAt,
                chargeBatch.BillingPeriodId,
                null,
                $"Lote {chargeBatch.Id} aprovado para o ambiente {chargeBatch.AsaasEnvironment}."),
            cancellationToken);

        return ChargeBatchResponse.FromDomain(chargeBatch);
    }

    public async Task<ChargeBatchResponse> ExecuteAsync(
        Guid chargeBatchId,
        ExecuteChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfirmationPhrase(request.ConfirmationPhrase);
        var chargeBatch = await FindChargeBatchAsync(chargeBatchId, cancellationToken);
        chargeBatch.StartProcessing(DateTimeOffset.UtcNow);
        await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "charge-batch.execution-started",
                request.OperatorId,
                chargeBatch.UpdatedAt,
                chargeBatch.BillingPeriodId,
                null,
                $"Execução do lote {chargeBatch.Id} iniciada no ambiente {chargeBatch.AsaasEnvironment}."),
            cancellationToken);

        foreach (var chargeBatchItem in chargeBatch.Items)
        {
            await ExecuteItemAsync(chargeBatch, chargeBatchItem.BillingDraftId, request.OperatorId, cancellationToken);
        }

        chargeBatch.MarkCompleted(DateTimeOffset.UtcNow);
        await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "charge-batch.completed",
                request.OperatorId,
                chargeBatch.UpdatedAt,
                chargeBatch.BillingPeriodId,
                null,
                $"Lote {chargeBatch.Id} finalizado com status {chargeBatch.Status}."),
            cancellationToken);

        return ChargeBatchResponse.FromDomain(chargeBatch);
    }

    public async Task<ChargeBatchResponse> RetryFailedAsync(
        Guid chargeBatchId,
        RetryFailedChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfirmationPhrase(request.ConfirmationPhrase);
        var originalChargeBatch = await FindChargeBatchAsync(chargeBatchId, cancellationToken);
        var failedBillingDraftIds = originalChargeBatch.Items
            .Where(item => item.Status == ChargeBatchItemStatus.Failed)
            .Select(item => item.BillingDraftId)
            .ToArray();
        if (failedBillingDraftIds.Length == 0)
        {
            throw new ConflictException("Este lote não possui itens com falha para repetir.");
        }

        var retryChargeBatch = await CreatePreviewForBillingDraftsAsync(
            request.OperatorId,
            originalChargeBatch.DueDate,
            originalChargeBatch.AsaasEnvironment,
            failedBillingDraftIds,
            originalChargeBatch.Id,
            cancellationToken);
        retryChargeBatch.Approve(request.OperatorId, DateTimeOffset.UtcNow);
        await chargeBatchRepository.UpdateAsync(retryChargeBatch, cancellationToken);

        return await ExecuteAsync(
            retryChargeBatch.Id,
            new ExecuteChargeBatchRequest(request.OperatorId, request.ConfirmationPhrase),
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<ChargeBatchResponse>> ListByBillingPeriodAsync(
        BillingPeriodReference billingPeriodReference,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");

        var chargeBatches = await chargeBatchRepository.ListByBillingPeriodIdAsync(
            billingPeriod.Id,
            cancellationToken);
        return chargeBatches.Select(ChargeBatchResponse.FromDomain).ToArray();
    }

    private async Task<ChargeBatch> CreatePreviewForBillingDraftsAsync(
        string operatorId,
        DateOnly dueDate,
        AsaasEnvironment asaasEnvironment,
        IReadOnlyCollection<Guid> billingDraftIds,
        Guid? retryOfChargeBatchId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela criação do lote é obrigatório.");
        }

        var billingDrafts = new List<BillingDraft>();
        foreach (var billingDraftId in billingDraftIds)
        {
            var billingDraft = await billingDraftRepository.FindByIdAsync(billingDraftId, cancellationToken)
                ?? throw new NotFoundException("Uma das prévias selecionadas não foi encontrada.");
            if (billingDraft.Status != BillingDraftStatus.Approved)
            {
                throw new ConflictException("Todas as prévias do lote precisam estar aprovadas.");
            }

            billingDrafts.Add(billingDraft);
        }

        var billingPeriodIds = billingDrafts.Select(billingDraft => billingDraft.BillingPeriodId).Distinct().ToArray();
        if (billingPeriodIds.Length != 1)
        {
            throw new ValidationException("Todas as prévias de um lote precisam pertencer à mesma competência.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var chargeBatch = new ChargeBatch(
            billingPeriodIds[0],
            dueDate,
            operatorId,
            asaasEnvironment,
            retryOfChargeBatchId,
            billingDraftIds,
            createdAt);
        await chargeBatchRepository.AddAsync(chargeBatch, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "charge-batch.preview-created",
                operatorId,
                createdAt,
                chargeBatch.BillingPeriodId,
                null,
                $"Prévia do lote {chargeBatch.Id} criada com {billingDraftIds.Count} cobrança(s) para {dueDate:yyyy-MM-dd} no ambiente {asaasEnvironment}."),
            cancellationToken);

        return chargeBatch;
    }

    private async Task ExecuteItemAsync(
        ChargeBatch chargeBatch,
        Guid billingDraftId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var chargeBatchItem = chargeBatch.GetItem(billingDraftId);
        try
        {
            var chargeCreationResult = await chargeCreationService.CreateAsync(
                billingDraftId,
                chargeBatch.DueDate,
                operatorId,
                ChargeCreationService.RequiredConfirmationPhrase,
                chargeBatch.AsaasEnvironment,
                cancellationToken);
            chargeBatchItem.MarkChargeCreated(
                chargeCreationResult.AsaasPaymentId,
                chargeCreationResult.BankSlipUrl,
                chargeCreationResult.CreatedNow,
                DateTimeOffset.UtcNow);
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "charge-batch.item.completed",
                    operatorId,
                    chargeBatchItem.UpdatedAt,
                    chargeBatch.BillingPeriodId,
                    billingDraftId,
                    $"Item da prévia concluído no lote {chargeBatch.Id}."),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failureMessage = GetFailureMessage(exception);
            chargeBatchItem.MarkFailed(failureMessage, DateTimeOffset.UtcNow);
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "charge-batch.item.failed",
                    operatorId,
                    chargeBatchItem.UpdatedAt,
                    chargeBatch.BillingPeriodId,
                    billingDraftId,
                    $"Item da prévia falhou no lote {chargeBatch.Id}: {failureMessage}"),
                cancellationToken);
        }

        await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
    }

    private async Task<ChargeBatch> FindChargeBatchAsync(Guid chargeBatchId, CancellationToken cancellationToken)
    {
        return await chargeBatchRepository.FindByIdAsync(chargeBatchId, cancellationToken)
            ?? throw new NotFoundException("O lote de cobrança não foi encontrado.");
    }

    private static Guid[] NormalizeBillingDraftIds(IReadOnlyCollection<Guid> requestedBillingDraftIds)
    {
        var billingDraftIds = requestedBillingDraftIds
            .Where(billingDraftId => billingDraftId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (billingDraftIds.Length == 0)
        {
            throw new ValidationException("Selecione ao menos uma prévia para criar o lote.");
        }

        return billingDraftIds;
    }

    private static AsaasEnvironment ParseAsaasEnvironment(string requestedAsaasEnvironment)
    {
        if (Enum.TryParse<AsaasEnvironment>(requestedAsaasEnvironment, true, out var asaasEnvironment))
        {
            return asaasEnvironment;
        }

        throw new ValidationException("Selecione o ambiente Asaas Sandbox ou Production.");
    }

    private static void EnsureConfirmationPhrase(string confirmationPhrase)
    {
        if (!string.Equals(
                confirmationPhrase?.Trim(),
                ChargeCreationService.RequiredConfirmationPhrase,
                StringComparison.Ordinal))
        {
            throw new ValidationException("Digite CONFIRMAR para autorizar a criação do lote.");
        }
    }

    private static string GetFailureMessage(Exception exception)
    {
        return exception is DomainException
            ? exception.Message
            : "Não foi possível concluir a cobrança. Tente novamente ou consulte o suporte técnico.";
    }
}
