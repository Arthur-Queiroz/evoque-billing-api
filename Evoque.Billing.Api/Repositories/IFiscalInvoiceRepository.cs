using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IFiscalInvoiceRepository
{
    Task AddAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken);

    Task UpdateAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken);

    Task<FiscalInvoice?> FindByIdAsync(Guid fiscalInvoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken);
}
