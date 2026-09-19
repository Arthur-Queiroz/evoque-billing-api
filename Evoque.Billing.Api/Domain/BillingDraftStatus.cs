namespace Evoque.Billing.Api.Domain;

public enum BillingDraftStatus
{
    PendingReview,
    Approved,
    ChargeCreated,
    Cancelled,
    Superseded,
}
