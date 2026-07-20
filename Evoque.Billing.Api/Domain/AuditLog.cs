namespace Evoque.Billing.Api.Domain;

public sealed record AuditLog(
    Guid Id,
    string Action,
    string OperatorId,
    DateTimeOffset OccurredAt,
    Guid? BillingPeriodId,
    Guid? BillingDraftId,
    string Details)
{
    public static AuditLog Create(
        string action,
        string operatorId,
        DateTimeOffset occurredAt,
        Guid? billingPeriodId,
        Guid? billingDraftId,
        string details)
    {
        return new AuditLog(
            Guid.NewGuid(),
            action,
            operatorId,
            occurredAt,
            billingPeriodId,
            billingDraftId,
            details);
    }
}
