namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Uma cobrança emitida por este sistema, com o que é preciso para encontrá-la:
/// quando saiu, para quem, em qual ambiente, e os documentos que ela produziu.
///
/// É um modelo de leitura. Reúne dados de <see cref="ChargeBatch"/>,
/// <see cref="ChargeBatchItem"/>, <see cref="BillingDraft"/> e
/// <see cref="FiscalInvoice"/>, e não é persistido em tabela própria.
/// </summary>
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
    FiscalInvoiceStatus? FiscalInvoiceStatus,
    string? FiscalInvoicePdfUrl,
    string? FiscalInvoiceErrorMessage);
