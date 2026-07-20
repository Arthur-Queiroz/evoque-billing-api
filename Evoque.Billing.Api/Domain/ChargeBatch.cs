namespace Evoque.Billing.Api.Domain;

public sealed class ChargeBatch
{
    private readonly List<ChargeBatchItem> items;

    public ChargeBatch(
        Guid billingPeriodId,
        DateOnly dueDate,
        string operatorId,
        AsaasEnvironment asaasEnvironment,
        Guid? retryOfChargeBatchId,
        IReadOnlyCollection<Guid> billingDraftIds,
        DateTimeOffset createdAt)
        : this(
            Guid.NewGuid(),
            billingPeriodId,
            dueDate,
            operatorId,
            asaasEnvironment,
            retryOfChargeBatchId,
            ChargeBatchStatus.AwaitingApproval,
            billingDraftIds.Select(billingDraftId => new ChargeBatchItem(billingDraftId, createdAt)).ToArray(),
            null,
            null,
            createdAt,
            createdAt)
    {
    }

    private ChargeBatch(
        Guid id,
        Guid billingPeriodId,
        DateOnly dueDate,
        string operatorId,
        AsaasEnvironment asaasEnvironment,
        Guid? retryOfChargeBatchId,
        ChargeBatchStatus status,
        IReadOnlyCollection<ChargeBatchItem> items,
        string? approvedBy,
        DateTimeOffset? approvedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (billingPeriodId == Guid.Empty)
        {
            throw new ValidationException("A competência do lote é obrigatória.");
        }

        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela criação do lote é obrigatório.");
        }

        if (items.Count == 0)
        {
            throw new ValidationException("Um lote deve conter pelo menos uma prévia.");
        }

        if (items.Select(item => item.BillingDraftId).Distinct().Count() != items.Count)
        {
            throw new ValidationException("Uma prévia não pode aparecer mais de uma vez no mesmo lote.");
        }

        Id = id;
        BillingPeriodId = billingPeriodId;
        DueDate = dueDate;
        OperatorId = operatorId;
        AsaasEnvironment = asaasEnvironment;
        RetryOfChargeBatchId = retryOfChargeBatchId;
        Status = status;
        this.items = items.ToList();
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid BillingPeriodId { get; }

    public DateOnly DueDate { get; }

    public string OperatorId { get; }

    public AsaasEnvironment AsaasEnvironment { get; }

    public Guid? RetryOfChargeBatchId { get; }

    public ChargeBatchStatus Status { get; private set; }

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public IReadOnlyCollection<ChargeBatchItem> Items => items;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChargeBatch Restore(
        Guid id,
        Guid billingPeriodId,
        DateOnly dueDate,
        string operatorId,
        AsaasEnvironment asaasEnvironment,
        Guid? retryOfChargeBatchId,
        ChargeBatchStatus status,
        IReadOnlyCollection<ChargeBatchItem> items,
        string? approvedBy,
        DateTimeOffset? approvedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new ChargeBatch(
            id,
            billingPeriodId,
            dueDate,
            operatorId,
            asaasEnvironment,
            retryOfChargeBatchId,
            status,
            items,
            approvedBy,
            approvedAt,
            createdAt,
            updatedAt);
    }

    public ChargeBatchItem GetItem(Guid billingDraftId)
    {
        return items.SingleOrDefault(item => item.BillingDraftId == billingDraftId)
            ?? throw new NotFoundException("A prévia não pertence a este lote de cobrança.");
    }

    public void MarkCompleted(DateTimeOffset updatedAt)
    {
        if (Status != ChargeBatchStatus.Processing)
        {
            throw new ConflictException("O lote precisa estar em processamento para ser concluído.");
        }

        if (items.Any(item => item.Status == ChargeBatchItemStatus.Pending))
        {
            throw new ConflictException("Não é possível concluir um lote com itens pendentes.");
        }

        Status = items.Any(item => item.Status == ChargeBatchItemStatus.Failed)
            ? ChargeBatchStatus.CompletedWithErrors
            : ChargeBatchStatus.Completed;
        UpdatedAt = updatedAt;
    }

    public void Approve(string operatorId, DateTimeOffset approvedAt)
    {
        if (Status != ChargeBatchStatus.AwaitingApproval)
        {
            throw new ConflictException("Somente lotes aguardando aprovação podem ser aprovados.");
        }

        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela aprovação do lote é obrigatório.");
        }

        ApprovedBy = operatorId;
        ApprovedAt = approvedAt;
        Status = ChargeBatchStatus.Approved;
        UpdatedAt = approvedAt;
    }

    public void StartProcessing(DateTimeOffset updatedAt)
    {
        if (Status != ChargeBatchStatus.Approved)
        {
            throw new ConflictException("O lote precisa estar aprovado antes de criar cobranças.");
        }

        Status = ChargeBatchStatus.Processing;
        UpdatedAt = updatedAt;
    }
}
