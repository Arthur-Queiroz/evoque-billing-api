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
            PaymentStatuses = ParsePaymentStatuses(query.PaymentStatus),
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

    /// <summary>
    /// Traduz o que a tela oferece, não o enum cru. "Pago" cobre `Received` e
    /// `Confirmed` porque o Asaas devolve os dois para a mesma coisa do ponto de
    /// vista de quem opera, e a coluna já os mostra com o mesmo rótulo.
    /// </summary>
    private static IReadOnlyCollection<ChargePaymentStatus>? ParsePaymentStatuses(
        string? requestedPaymentStatus)
    {
        return requestedPaymentStatus switch
        {
            null or "" => null,
            "unknown" => [ChargePaymentStatus.Unknown],
            "pending" => [ChargePaymentStatus.Pending],
            "paid" => [ChargePaymentStatus.Received, ChargePaymentStatus.Confirmed],
            "overdue" => [ChargePaymentStatus.Overdue],
            "refunded" => [ChargePaymentStatus.RefundRequested, ChargePaymentStatus.Refunded],
            _ => throw new ValidationException("A situação do boleto informada no filtro não existe."),
        };
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
