using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

public sealed record CreateBillingPeriodRequest(string OperatorId);

public sealed record BillingPeriodResponse(
    Guid Id,
    int Year,
    int Month,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BillingPeriodResponse FromDomain(BillingPeriod billingPeriod)
    {
        return new BillingPeriodResponse(
            billingPeriod.Id,
            billingPeriod.Reference.Year,
            billingPeriod.Reference.Month,
            billingPeriod.Status.ToString(),
            billingPeriod.CreatedAt,
            billingPeriod.UpdatedAt);
    }
}
