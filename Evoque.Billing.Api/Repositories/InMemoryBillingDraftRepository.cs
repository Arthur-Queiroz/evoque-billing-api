using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryBillingDraftRepository(InMemoryBillingDataStore dataStore) : IBillingDraftRepository
{
    public Task AddAsync(BillingDraft billingDraft, CancellationToken cancellationToken)
    {
        if (!dataStore.BillingDrafts.TryAdd(billingDraft.Id, billingDraft))
        {
            throw new ConflictException("A prévia de faturamento já existe.");
        }

        return Task.CompletedTask;
    }

    public Task<BillingDraft?> FindByIdAsync(Guid billingDraftId, CancellationToken cancellationToken)
    {
        dataStore.BillingDrafts.TryGetValue(billingDraftId, out var billingDraft);
        return Task.FromResult(billingDraft);
    }

    public Task<IReadOnlyCollection<BillingDraft>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        var billingDrafts = dataStore.BillingDrafts.Values
            .Where(billingDraft => billingDraft.BillingPeriodId == billingPeriodId)
            .OrderBy(billingDraft => billingDraft.CompanyName)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<BillingDraft>>(billingDrafts);
    }

    public Task<IReadOnlyCollection<BillingDraft>> ListByExternalCompanyIdAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var billingDrafts = dataStore.BillingDrafts.Values
            .Where(billingDraft => billingDraft.ExternalCompanyId == externalCompanyId)
            .OrderByDescending(billingDraft => billingDraft.CreatedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<BillingDraft>>(billingDrafts);
    }

    public Task UpdateAsync(BillingDraft billingDraft, CancellationToken cancellationToken)
    {
        dataStore.BillingDrafts[billingDraft.Id] = billingDraft;
        return Task.CompletedTask;
    }
}
