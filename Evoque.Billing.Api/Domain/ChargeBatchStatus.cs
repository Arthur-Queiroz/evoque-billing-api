namespace Evoque.Billing.Api.Domain;

public enum ChargeBatchStatus
{
    AwaitingApproval,
    Approved,
    Processing,
    Completed,
    CompletedWithErrors,
}
