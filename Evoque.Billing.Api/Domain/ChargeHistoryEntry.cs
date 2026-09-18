namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Uma cobrança emitida por este sistema, com o que é preciso para encontrá-la:
/// quando saiu, para quem, em qual ambiente, e o boleto e a nota que ela gerou.
///
/// É um modelo de leitura. Reúne dados de <see cref="ChargeBatch"/>,
/// <see cref="ChargeBatchItem"/>, <see cref="BillingDraft"/> e
/// <see cref="FiscalInvoice"/>, e não é persistido em tabela própria.
/// </summary>
/// <param name="IssuedAt">
/// Data de criação do lote, e a chave da ordenação do histórico. Vale para toda
/// linha, inclusive as que falharam: uma tentativa recusada também aconteceu
/// naquele dia. Não é o instante da chamada ao Asaas, e é deliberado — é o único
/// carimbo de tempo aqui que nenhuma sincronização posterior reescreve.
/// </param>
/// <param name="FiscalInvoicePdfUrl">
/// Só o PDF. O XML existe em <see cref="FiscalInvoice.XmlUrl"/> e fica de fora:
/// quem procura uma nota no histórico quer o documento que se lê.
/// </param>
public sealed record ChargeHistoryEntry(
    Guid ChargeBatchId,
    Guid BillingDraftId,
    BillingPeriodReference BillingPeriodReference,
    AsaasEnvironment AsaasEnvironment,
    string CompanyName,
    string CompanyTaxId,
    decimal TotalAmount,
    int MemberCount,
    DateOnly DueDate,
    DateTimeOffset IssuedAt,
    ChargeBatchItemStatus ItemStatus,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? ItemErrorMessage,
    ChargePaymentStatus PaymentStatus,
    DateOnly? PaidAt,
    FiscalInvoiceStatus? FiscalInvoiceStatus,
    string? FiscalInvoicePdfUrl,
    string? FiscalInvoiceErrorMessage);
