using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IBillingDraftRepository
{
    Task AddAsync(BillingDraft billingDraft, CancellationToken cancellationToken);

    Task<BillingDraft?> FindByIdAsync(Guid billingDraftId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BillingDraft>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Histórico de faturamento de uma empresa do catálogo. O identificador é o
    /// CNPJ normalizado, o mesmo que a importação de fechamento já grava.
    /// </summary>
    Task<IReadOnlyCollection<BillingDraft>> ListByExternalCompanyIdAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);

    Task UpdateAsync(BillingDraft billingDraft, CancellationToken cancellationToken);
}
