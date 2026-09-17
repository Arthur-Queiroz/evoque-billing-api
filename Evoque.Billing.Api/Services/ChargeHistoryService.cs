using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Responde o que este sistema emitiu. Somente leitura: nenhuma operação aqui
/// altera cobrança, prévia ou nota.
/// </summary>
public sealed class ChargeHistoryService(IChargeHistoryRepository chargeHistoryRepository)
{
    public async Task<IReadOnlyCollection<ChargeHistoryEntryResponse>> ListAsync(
        ChargeHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var filter = new ChargeHistoryFilter
        {
            CompanySearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            AsaasEnvironment = ParseAsaasEnvironment(query.Environment),
            BillingPeriodReference = ParseBillingPeriodReference(query.Year, query.Month),
        };

        var entries = await chargeHistoryRepository.ListAsync(filter, cancellationToken);
        return entries.Select(ChargeHistoryEntryResponse.FromDomain).ToArray();
    }

    private static AsaasEnvironment? ParseAsaasEnvironment(string? requestedEnvironment)
    {
        if (string.IsNullOrWhiteSpace(requestedEnvironment))
        {
            return null;
        }

        if (Enum.TryParse<AsaasEnvironment>(requestedEnvironment, true, out var asaasEnvironment))
        {
            return asaasEnvironment;
        }

        throw new ValidationException("O ambiente do filtro deve ser Sandbox ou Production.");
    }

    private static BillingPeriodReference? ParseBillingPeriodReference(int? year, int? month)
    {
        if (year is null && month is null)
        {
            return null;
        }

        if (year is null || month is null)
        {
            throw new ValidationException("Informe ano e mês juntos para filtrar por competência.");
        }

        return new BillingPeriodReference(year.Value, month.Value);
    }
}
