using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryBillingPeriodRepository(InMemoryBillingDataStore dataStore) : IBillingPeriodRepository
{
    public Task AddAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken)
    {
        if (!dataStore.BillingPeriods.TryAdd(billingPeriod.Reference.ToString(), billingPeriod))
        {
            throw new ConflictException("A competência já existe.");
        }

        return Task.CompletedTask;
    }

    public Task<BillingPeriod?> FindByReferenceAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken)
    {
        dataStore.BillingPeriods.TryGetValue(reference.ToString(), out var billingPeriod);
        return Task.FromResult(billingPeriod);
    }

    public Task<IReadOnlyCollection<BillingPeriod>> ListAsync(CancellationToken cancellationToken)
    {
        var billingPeriods = dataStore.BillingPeriods.Values
            .OrderByDescending(billingPeriod => billingPeriod.Reference.Year)
            .ThenByDescending(billingPeriod => billingPeriod.Reference.Month)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<BillingPeriod>>(billingPeriods);
    }

    public Task UpdateAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken)
    {
        dataStore.BillingPeriods[billingPeriod.Reference.ToString()] = billingPeriod;
        return Task.CompletedTask;
    }
}
