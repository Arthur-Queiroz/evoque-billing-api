using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

/// <summary>Reemissão de uma nota recusada. Exige a frase CONFIRMAR.</summary>
public sealed record ReissueFiscalInvoiceRequest(string OperatorId, string ConfirmationPhrase);

public sealed record SynchronizeFiscalInvoicesRequest(string OperatorId);

public sealed record FiscalInvoiceResponse(
    Guid Id,
    Guid BillingDraftId,
    Guid BillingPeriodId,
    int Sequence,
    string AsaasPaymentId,
    string? AsaasInvoiceId,
    string Status,
    decimal TotalAmount,
    DateOnly EffectiveDate,
    bool RetainsIss,
    string ServiceDescription,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FiscalInvoiceResponse FromDomain(FiscalInvoice fiscalInvoice)
    {
        return new FiscalInvoiceResponse(
            fiscalInvoice.Id,
            fiscalInvoice.BillingDraftId,
            fiscalInvoice.BillingPeriodId,
            fiscalInvoice.Sequence,
            fiscalInvoice.AsaasPaymentId,
            fiscalInvoice.AsaasInvoiceId,
            fiscalInvoice.Status.ToString(),
            fiscalInvoice.TotalAmount,
            fiscalInvoice.EffectiveDate,
            fiscalInvoice.RetainsIss,
            fiscalInvoice.ServiceDescription,
            fiscalInvoice.ErrorMessage,
            fiscalInvoice.CreatedAt,
            fiscalInvoice.UpdatedAt);
    }
}
