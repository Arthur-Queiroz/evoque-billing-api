using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IBillingDraftRepository
{
    Task AddAsync(BillingDraft billingDraft, CancellationToken cancellationToken);

    Task<BillingDraft?> FindByIdAsync(Guid billingDraftId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BillingDraft>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken);

    Task UpdateAsync(BillingDraft billingDraft, CancellationToken cancellationToken);
}
