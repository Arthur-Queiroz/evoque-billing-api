using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryChargeBatchRepository(InMemoryBillingDataStore dataStore) : IChargeBatchRepository
{
    public Task AddAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
    {
        if (!dataStore.ChargeBatches.TryAdd(chargeBatch.Id, chargeBatch))
        {
            throw new ConflictException("O lote de cobrança já existe.");
        }

        return Task.CompletedTask;
    }

    public Task<ChargeBatch?> FindByIdAsync(Guid chargeBatchId, CancellationToken cancellationToken)
    {
        dataStore.ChargeBatches.TryGetValue(chargeBatchId, out var chargeBatch);
        return Task.FromResult(chargeBatch);
    }

    public Task<IReadOnlyCollection<ChargeBatch>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        var chargeBatches = dataStore.ChargeBatches.Values
            .Where(chargeBatch => chargeBatch.BillingPeriodId == billingPeriodId)
            .OrderByDescending(chargeBatch => chargeBatch.CreatedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<ChargeBatch>>(chargeBatches);
    }

    public Task UpdateAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
    {
        dataStore.ChargeBatches[chargeBatch.Id] = chargeBatch;
        return Task.CompletedTask;
    }
}
