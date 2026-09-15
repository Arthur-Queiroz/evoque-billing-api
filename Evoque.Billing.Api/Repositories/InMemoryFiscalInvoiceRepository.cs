using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryFiscalInvoiceRepository(InMemoryBillingDataStore dataStore)
    : IFiscalInvoiceRepository
{
    public Task AddAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
    {
        // Espelha a chave única de fiscal_invoices no MySQL. Sem essa checagem,
        // duas execuções concorrentes do mesmo lote emitiriam duas notas para a
        // mesma prévia, e nota duplicada na prefeitura só se desfaz com
        // cancelamento — que ela já negou por competência encerrada.
        var alreadyHasTheSameSequence = dataStore.FiscalInvoices.Values.Any(existingInvoice =>
            existingInvoice.BillingDraftId == fiscalInvoice.BillingDraftId
            && existingInvoice.Sequence == fiscalInvoice.Sequence);
        if (alreadyHasTheSameSequence)
        {
            throw new ConflictException("Já existe uma nota fiscal com essa sequência para a prévia.");
        }

        if (!dataStore.FiscalInvoices.TryAdd(fiscalInvoice.Id, fiscalInvoice))
        {
            throw new ConflictException("A nota fiscal já existe.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
    {
        dataStore.FiscalInvoices[fiscalInvoice.Id] = fiscalInvoice;
        return Task.CompletedTask;
    }

    public Task<FiscalInvoice?> FindByIdAsync(Guid fiscalInvoiceId, CancellationToken cancellationToken)
    {
        dataStore.FiscalInvoices.TryGetValue(fiscalInvoiceId, out var fiscalInvoice);
        return Task.FromResult(fiscalInvoice);
    }

    public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = dataStore.FiscalInvoices.Values
            .Where(fiscalInvoice => fiscalInvoice.BillingDraftId == billingDraftId)
            .OrderBy(fiscalInvoice => fiscalInvoice.Sequence)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<FiscalInvoice>>(fiscalInvoices);
    }

    public Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = dataStore.FiscalInvoices.Values
            .Where(fiscalInvoice => fiscalInvoice.BillingPeriodId == billingPeriodId)
            .OrderByDescending(fiscalInvoice => fiscalInvoice.CreatedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<FiscalInvoice>>(fiscalInvoices);
    }
}
