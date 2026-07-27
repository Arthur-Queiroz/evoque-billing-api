namespace Evoque.Billing.Api.Domain;

public sealed class BillingPeriod
{
    public BillingPeriod(BillingPeriodReference reference, DateTimeOffset createdAt)
        : this(Guid.NewGuid(), reference, BillingPeriodStatus.Open, createdAt, createdAt)
    {
    }

    private BillingPeriod(
        Guid id,
        BillingPeriodReference reference,
        BillingPeriodStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Reference = reference;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public BillingPeriodReference Reference { get; }

    public BillingPeriodStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static BillingPeriod Restore(
        Guid id,
        BillingPeriodReference reference,
        BillingPeriodStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new BillingPeriod(id, reference, status, createdAt, updatedAt);
    }

    public void MarkAwaitingReview(DateTimeOffset updatedAt)
    {
        if (Status == BillingPeriodStatus.ChargesCreated)
        {
            throw new ConflictException("Não é possível alterar uma competência já encerrada.");
        }

        Status = BillingPeriodStatus.AwaitingReview;
        UpdatedAt = updatedAt;
    }

    public void MarkApproved(DateTimeOffset updatedAt)
    {
        if (Status != BillingPeriodStatus.AwaitingReview)
        {
            throw new ConflictException("A competência precisa estar aguardando revisão antes de ser aprovada.");
        }

        Status = BillingPeriodStatus.Approved;
        UpdatedAt = updatedAt;
    }

    public void MarkChargesCreated(DateTimeOffset updatedAt)
    {
        if (Status != BillingPeriodStatus.Approved)
        {
            throw new ConflictException("A competência precisa estar aprovada antes de criar cobranças.");
        }

        Status = BillingPeriodStatus.ChargesCreated;
        UpdatedAt = updatedAt;
    }
}
