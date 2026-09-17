using Evoque.Billing.Api.Domain;

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
            // Defensivo, não um caminho esperado: um lote sempre referencia uma
            // competência existente. Se isto disparar, a linha some do histórico
            // sem nenhum rastro — é a primeira pista para quem for investigar um
            // "sumiu do histórico".
            if (!billingPeriodsById.TryGetValue(chargeBatch.BillingPeriodId, out var billingPeriod))
            {
                continue;
            }

            foreach (var chargeBatchItem in chargeBatch.Items)
            {
                // Mesma natureza defensiva do skip acima: um item de lote sempre
                // referencia uma prévia existente. Um disparo aqui também apaga
                // uma linha do histórico em silêncio.
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
            ApplyFilter(entries, filter)
                .OrderByDescending(entry => entry.IssuedAt)
                .ToArray());
    }

    private static IEnumerable<ChargeHistoryEntry> ApplyFilter(
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
            var searchTerm = filter.CompanySearch.Trim();

            // Nome e CNPJ são buscas mutuamente exclusivas pela forma do termo:
            // quem digita busca por um ou por outro, nunca por uma mistura dos
            // dois. Sem essa separação, um dígito solto dentro de um nome (ex.:
            // "Farmava 2") virava candidato a CNPJ, e `CompanyTaxId.Contains`
            // casava com qualquer empresa cujo CNPJ contivesse aquele dígito —
            // na prática, quase toda empresa do banco.
            entries = IsTaxIdShaped(searchTerm)
                ? FilterByTaxId(entries, searchTerm)
                : FilterByCompanyName(entries, searchTerm);
        }

        return entries;
    }

    /// <summary>
    /// Um termo só é CNPJ se for feito inteiramente de dígitos e da pontuação
    /// usual do CNPJ. Qualquer letra no meio já indica busca por nome.
    /// </summary>
    private static bool IsTaxIdShaped(string searchTerm)
    {
        return searchTerm.All(character =>
            char.IsAsciiDigit(character) || character is '.' or '/' or '-' or ' ');
    }

    private static IEnumerable<ChargeHistoryEntry> FilterByTaxId(
        IEnumerable<ChargeHistoryEntry> entries,
        string searchTerm)
    {
        var digitsOnly = new string(searchTerm.Where(char.IsAsciiDigit).ToArray());
        return digitsOnly.Length == 0
            ? []
            : entries.Where(entry => entry.CompanyTaxId.Contains(digitsOnly, StringComparison.Ordinal));
    }

    private static IEnumerable<ChargeHistoryEntry> FilterByCompanyName(
        IEnumerable<ChargeHistoryEntry> entries,
        string searchTerm)
    {
        var normalizedSearchTerm = TextNormalization.Normalize(searchTerm);
        return entries.Where(entry =>
            TextNormalization.Normalize(entry.CompanyName).Contains(normalizedSearchTerm, StringComparison.Ordinal));
    }
}
