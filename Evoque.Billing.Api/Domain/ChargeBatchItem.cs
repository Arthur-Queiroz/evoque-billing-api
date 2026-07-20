namespace Evoque.Billing.Api.Domain;

public sealed class ChargeBatchItem
{
    public ChargeBatchItem(Guid billingDraftId, DateTimeOffset updatedAt)
        : this(
            billingDraftId,
            ChargeBatchItemStatus.Pending,
            null,
            null,
            null,
            updatedAt)
    {
    }

    private ChargeBatchItem(
        Guid billingDraftId,
        ChargeBatchItemStatus status,
        string? asaasPaymentId,
        string? bankSlipUrl,
        string? errorMessage,
        DateTimeOffset updatedAt)
    {
        if (billingDraftId == Guid.Empty)
        {
            throw new ValidationException("O identificador da prévia é obrigatório no lote de cobrança.");
        }

        BillingDraftId = billingDraftId;
        Status = status;
        AsaasPaymentId = asaasPaymentId;
        BankSlipUrl = bankSlipUrl;
        ErrorMessage = errorMessage;
        UpdatedAt = updatedAt;
    }

    public Guid BillingDraftId { get; }

    public ChargeBatchItemStatus Status { get; private set; }

    public string? AsaasPaymentId { get; private set; }

    public string? BankSlipUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChargeBatchItem Restore(
        Guid billingDraftId,
        ChargeBatchItemStatus status,
        string? asaasPaymentId,
        string? bankSlipUrl,
        string? errorMessage,
        DateTimeOffset updatedAt)
    {
        return new ChargeBatchItem(
            billingDraftId,
            status,
            asaasPaymentId,
            bankSlipUrl,
            errorMessage,
            updatedAt);
    }

    public void MarkChargeCreated(
        string asaasPaymentId,
        string? bankSlipUrl,
        bool createdNow,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(asaasPaymentId))
        {
            throw new ValidationException("O identificador da cobrança Asaas é obrigatório.");
        }

        Status = createdNow ? ChargeBatchItemStatus.Created : ChargeBatchItemStatus.AlreadyExists;
        AsaasPaymentId = asaasPaymentId;
        BankSlipUrl = bankSlipUrl;
        ErrorMessage = null;
        UpdatedAt = updatedAt;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ValidationException("O motivo da falha no lote é obrigatório.");
        }

        Status = ChargeBatchItemStatus.Failed;
        AsaasPaymentId = null;
        BankSlipUrl = null;
        ErrorMessage = errorMessage;
        UpdatedAt = updatedAt;
    }
}
