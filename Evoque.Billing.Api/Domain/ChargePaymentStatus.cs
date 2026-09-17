namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Situação de pagamento da cobrança no Asaas. `Unknown` é o estado de uma
/// cobrança que ainda não foi consultada — diferente de `Pending`, que é uma
/// resposta do Asaas.
/// </summary>
public enum ChargePaymentStatus
{
    Unknown,
    Pending,
    Received,
    Confirmed,
    Overdue,
    Refunded,
}
