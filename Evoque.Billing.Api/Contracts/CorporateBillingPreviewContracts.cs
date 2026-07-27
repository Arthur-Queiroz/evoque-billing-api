namespace Evoque.Billing.Api.Contracts;

public sealed record CreateCorporateBillingPreviewRequest(
    int Year,
    int Month,
    int ReceivableLimit = 50,
    int SaleLookupLimit = 20,
    int ReceivableSkip = 0);

public sealed record CorporateBillingPreviewResponse(
    int Year,
    int Month,
    int ReceivableSkip,
    int ReceivablesRead,
    int DuplicateReceivablesIgnored,
    int DistinctSalesFound,
    int SalesLookedUp,
    bool IsComplete,
    string CompletionMessage,
    IReadOnlyCollection<CorporateBillingCompanyPreviewResponse> Companies,
    IReadOnlyCollection<CorporateBillingPreviewExceptionResponse> Exceptions);

public sealed record CorporateBillingCompanyPreviewResponse(
    int PartnershipId,
    string PartnershipName,
    int MemberCount,
    int ReceivableCount,
    long TotalAmountCents,
    decimal TotalAmount,
    IReadOnlyCollection<CorporateReceivablePreviewResponse> Receivables);

public sealed record CorporateReceivablePreviewResponse(
    int ReceivableId,
    int SaleId,
    int MemberId,
    string PayerName,
    DateOnly? CompetenceDate,
    DateOnly? DueDate,
    long AmountCents,
    decimal Amount,
    int? StatusId,
    string? StatusName,
    int? PaymentTypeId,
    string? PaymentTypeName,
    int? CurrentInstallment,
    int? TotalInstallments,
    int? MemberMembershipId);

public sealed record CorporateBillingPreviewExceptionResponse(
    string Code,
    string Message,
    int? ReceivableId,
    int? SaleId);
