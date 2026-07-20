using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IBillingPeriodRepository
{
    Task AddAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken);

    Task<BillingPeriod?> FindByReferenceAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BillingPeriod>> ListAsync(CancellationToken cancellationToken);

    Task UpdateAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken);
}
