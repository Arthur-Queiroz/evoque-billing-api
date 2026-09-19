using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Contracts;

public sealed record CreateBillingDraftRequest(
    string ExternalCompanyId,
    string CompanyName,
    string CompanyTaxId,
    string? AsaasCustomerId,
    IReadOnlyCollection<CreateBillingDraftItemRequest> Items)
{
    public CreateBillingDraftCommand ToCommand()
    {
        return new CreateBillingDraftCommand(
            ExternalCompanyId,
            CompanyName,
            CompanyTaxId,
            AsaasCustomerId,
            Items.Select(item => new CreateBillingDraftItemCommand(
                item.Description,
                item.Quantity,
                item.UnitAmount,
                item.ExternalMemberId)).ToArray());
    }
}

public sealed record CreateBillingDraftItemRequest(
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    string? ExternalMemberId);

public sealed record CreateChargeRequest(DateOnly DueDate, string ConfirmationPhrase);

public sealed record CancelBillingDraftRequest(string Reason);

public sealed record SupersedeBillingDraftRequest(string Reason);

public sealed record CreateChargeBatchRequest(
    DateOnly DueDate,
    string ConfirmationPhrase,
    IReadOnlyCollection<Guid> BillingDraftIds);

public sealed record CreateChargeBatchPreviewRequest(
    DateOnly DueDate,
    string AsaasEnvironment,
    IReadOnlyCollection<Guid> BillingDraftIds);

public sealed record ExecuteChargeBatchRequest(string ConfirmationPhrase);

public sealed record RetryFailedChargeBatchRequest(string ConfirmationPhrase);

public sealed record ChargeBatchItemResponse(
    Guid BillingDraftId,
    string Status,
    bool Created,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? Error)
{
    public static ChargeBatchItemResponse FromDomain(ChargeBatchItem chargeBatchItem)
    {
        return new ChargeBatchItemResponse(
            chargeBatchItem.BillingDraftId,
            chargeBatchItem.Status.ToString(),
            chargeBatchItem.Status == ChargeBatchItemStatus.Created,
            chargeBatchItem.AsaasPaymentId,
            chargeBatchItem.BankSlipUrl,
            chargeBatchItem.ErrorMessage);
    }
}

public sealed record ChargeBatchResponse(
    Guid Id,
    Guid BillingPeriodId,
    DateOnly DueDate,
    string AsaasEnvironment,
    string Status,
    Guid? RetryOfChargeBatchId,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ChargeBatchItemResponse> Items)
{
    public static ChargeBatchResponse FromDomain(ChargeBatch chargeBatch)
    {
        return new ChargeBatchResponse(
            chargeBatch.Id,
            chargeBatch.BillingPeriodId,
            chargeBatch.DueDate,
            chargeBatch.AsaasEnvironment.ToString(),
            chargeBatch.Status.ToString(),
            chargeBatch.RetryOfChargeBatchId,
            chargeBatch.ApprovedBy,
            chargeBatch.ApprovedAt,
            chargeBatch.CreatedAt,
            chargeBatch.UpdatedAt,
            chargeBatch.Items.Select(ChargeBatchItemResponse.FromDomain).ToArray());
    }
}

public sealed record BillingDraftItemResponse(
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    decimal TotalAmount,
    string? ExternalMemberId);

public sealed record BillingDraftResponse(
    Guid Id,
    Guid BillingPeriodId,
    string ExternalCompanyId,
    string CompanyName,
    string CompanyTaxId,
    string? AsaasCustomerId,
    decimal TotalAmount,
    string Status,
    int Version,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string? SupersededBy,
    DateTimeOffset? SupersededAt,
    string? SupersessionReason,
    IReadOnlyCollection<BillingDraftItemResponse> Items)
{
    public static BillingDraftResponse FromDomain(BillingDraft billingDraft)
    {
        return new BillingDraftResponse(
            billingDraft.Id,
            billingDraft.BillingPeriodId,
            billingDraft.ExternalCompanyId,
            billingDraft.CompanyName,
            billingDraft.CompanyTaxId,
            billingDraft.AsaasCustomerId,
            billingDraft.TotalAmount,
            billingDraft.Status.ToString(),
            billingDraft.Version,
            billingDraft.ApprovedBy,
            billingDraft.ApprovedAt,
            billingDraft.AsaasPaymentId,
            billingDraft.BankSlipUrl,
            billingDraft.CancelledBy,
            billingDraft.CancelledAt,
            billingDraft.CancellationReason,
            billingDraft.SupersededBy,
            billingDraft.SupersededAt,
            billingDraft.SupersessionReason,
            billingDraft.Items.Select(item => new BillingDraftItemResponse(
                item.Description,
                item.Quantity,
                item.UnitAmount,
                item.TotalAmount,
                item.ExternalMemberId)).ToArray());
    }
}

public sealed record AuditLogResponse(
    Guid Id,
    string Action,
    string OperatorId,
    DateTimeOffset OccurredAt,
    string Details)
{
    public static AuditLogResponse FromDomain(AuditLog auditLog)
    {
        return new AuditLogResponse(
            auditLog.Id,
            auditLog.Action,
            auditLog.OperatorId,
            auditLog.OccurredAt,
            auditLog.Details);
    }
}
