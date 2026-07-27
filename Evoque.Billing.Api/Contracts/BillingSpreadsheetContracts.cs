namespace Evoque.Billing.Api.Contracts;

public sealed record BillingSpreadsheetPreviewResponse(
    string FileName,
    int ImportedRowCount,
    int DuplicateRowCount,
    decimal TotalAmount,
    IReadOnlyCollection<BillingSpreadsheetCompanyResponse> Companies,
    IReadOnlyCollection<BillingSpreadsheetWarningResponse> Warnings);

public sealed record BillingSpreadsheetCompanyResponse(
    string CompanyName,
    string CompanyTaxId,
    int MemberCount,
    decimal TotalAmount,
    IReadOnlyCollection<BillingSpreadsheetMemberResponse> Members);

public sealed record BillingSpreadsheetMemberResponse(
    string MemberName,
    string ContractName,
    decimal Amount,
    int SourceRowNumber);

public sealed record BillingSpreadsheetWarningResponse(
    int? SourceRowNumber,
    string Code,
    string Message);

public sealed record BillingSpreadsheetDraftImportResponse(
    BillingSpreadsheetPreviewResponse SpreadsheetPreview,
    IReadOnlyCollection<BillingDraftResponse> BillingDrafts);
