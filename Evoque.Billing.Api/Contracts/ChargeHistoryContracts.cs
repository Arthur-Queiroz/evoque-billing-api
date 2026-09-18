using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

/// <summary>Filtros aceitos por `GET /api/charge-history`.</summary>
public sealed record ChargeHistoryQuery(
    string? Search = null,
    string? Environment = null,
    int? Year = null,
    int? Month = null);

public sealed record ChargeHistoryEntryResponse(
    Guid ChargeBatchId,
    Guid BillingDraftId,
    int Year,
    int Month,
    string AsaasEnvironment,
    string CompanyName,
    string CompanyTaxId,
    string FormattedCompanyTaxId,
    decimal TotalAmount,
    int MemberCount,
    DateOnly DueDate,
    DateTimeOffset IssuedAt,
    string ItemStatus,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? ItemErrorMessage,
    string PaymentStatus,
    DateOnly? PaidAt,
    string? FiscalInvoiceStatus,
    string? FiscalInvoicePdfUrl,
    string? FiscalInvoiceErrorMessage)
{
    public static ChargeHistoryEntryResponse FromDomain(ChargeHistoryEntry entry)
    {
        return new ChargeHistoryEntryResponse(
            entry.ChargeBatchId,
            entry.BillingDraftId,
            entry.BillingPeriodReference.Year,
            entry.BillingPeriodReference.Month,
            entry.AsaasEnvironment.ToString(),
            entry.CompanyName,
            entry.CompanyTaxId,
            // Referenciar `CompanyTaxId.Format` sem qualificar colidiria com a
            // própria propriedade `CompanyTaxId` deste record (CS0120): dentro de
            // um método estático o nome simples não enxerga a instância, mas o
            // compilador já resolveu o identificador para a propriedade.
            Domain.CompanyTaxId.Format(entry.CompanyTaxId),
            entry.TotalAmount,
            entry.MemberCount,
            entry.DueDate,
            entry.IssuedAt,
            entry.ItemStatus.ToString(),
            entry.AsaasPaymentId,
            entry.BankSlipUrl,
            entry.ItemErrorMessage,
            entry.PaymentStatus.ToString(),
            entry.PaidAt,
            entry.FiscalInvoiceStatus?.ToString(),
            entry.FiscalInvoicePdfUrl,
            entry.FiscalInvoiceErrorMessage);
    }
}
