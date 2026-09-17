using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryChargeHistoryRepository(InMemoryBillingDataStore dataStore)
    : IChargeHistoryRepository
{
    public Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var billingPeriodsById = dataStore.BillingPeriods.Values
            .ToDictionary(billingPeriod => billingPeriod.Id);

        var entries = new List<ChargeHistoryEntry>();
        foreach (var chargeBatch in dataStore.ChargeBatches.Values)
        {
            if (!billingPeriodsById.TryGetValue(chargeBatch.BillingPeriodId, out var billingPeriod))
            {
                continue;
            }

            foreach (var chargeBatchItem in chargeBatch.Items)
            {
                if (!dataStore.BillingDrafts.TryGetValue(chargeBatchItem.BillingDraftId, out var billingDraft))
                {
                    continue;
                }

                var fiscalInvoice = dataStore.FiscalInvoices.Values
                    .Where(invoice => invoice.BillingDraftId == billingDraft.Id)
                    .OrderByDescending(invoice => invoice.Sequence)
                    .FirstOrDefault();

                entries.Add(new ChargeHistoryEntry(
                    chargeBatch.Id,
                    billingDraft.Id,
                    billingPeriod.Reference,
                    chargeBatch.AsaasEnvironment,
                    billingDraft.CompanyName,
                    billingDraft.CompanyTaxId,
                    billingDraft.TotalAmount,
                    billingDraft.Items.Count,
                    chargeBatch.DueDate,
                    chargeBatch.CreatedAt,
                    chargeBatchItem.Status,
                    chargeBatchItem.AsaasPaymentId,
                    chargeBatchItem.BankSlipUrl,
                    chargeBatchItem.ErrorMessage,
                    fiscalInvoice?.Status,
                    fiscalInvoice?.PdfUrl,
                    fiscalInvoice?.ErrorMessage));
            }
        }

        return Task.FromResult<IReadOnlyCollection<ChargeHistoryEntry>>(
            Filtrar(entries, filter)
                .OrderByDescending(entry => entry.IssuedAt)
                .ToArray());
    }

    private static IEnumerable<ChargeHistoryEntry> Filtrar(
        IEnumerable<ChargeHistoryEntry> entries,
        ChargeHistoryFilter filter)
    {
        if (filter.AsaasEnvironment is not null)
        {
            entries = entries.Where(entry => entry.AsaasEnvironment == filter.AsaasEnvironment);
        }

        if (filter.BillingPeriodReference is not null)
        {
            entries = entries.Where(entry => entry.BillingPeriodReference == filter.BillingPeriodReference);
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            var termo = SpreadsheetText.Normalize(filter.CompanySearch);
            var apenasDigitos = new string(filter.CompanySearch.Where(char.IsAsciiDigit).ToArray());
            entries = entries.Where(entry =>
                SpreadsheetText.Normalize(entry.CompanyName).Contains(termo, StringComparison.Ordinal)
                || (apenasDigitos.Length > 0 && entry.CompanyTaxId.Contains(apenasDigitos, StringComparison.Ordinal)));
        }

        return entries;
    }
}
