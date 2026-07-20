using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IChargeBatchRepository
{
    Task AddAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken);

    Task<ChargeBatch?> FindByIdAsync(Guid chargeBatchId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChargeBatch>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken);

    Task UpdateAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken);
}
