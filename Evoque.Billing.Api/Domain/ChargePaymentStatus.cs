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

    /// <summary>
    /// Estorno pedido, ainda não decidido. Existe separado de
    /// <see cref="Refunded"/> porque o Asaas pode negar o pedido e devolver a
    /// cobrança para `RECEIVED`/`CONFIRMED`: tratar isso como estado final
    /// pararia a sincronização e deixaria a cobrança presa mostrando
    /// "estornada" para sempre, mesmo com o dinheiro recebido.
    /// </summary>
    RefundRequested,

    Refunded,
}
