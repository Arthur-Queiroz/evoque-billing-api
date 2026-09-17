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
            ChargePaymentStatus.Unknown,
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
        ChargePaymentStatus paymentStatus,
        DateOnly? paidAt,
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
        PaymentStatus = paymentStatus;
        PaidAt = paidAt;
        UpdatedAt = updatedAt;
    }

    public Guid BillingDraftId { get; }

    public ChargeBatchItemStatus Status { get; private set; }

    public string? AsaasPaymentId { get; private set; }

    public string? BankSlipUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Situação do boleto no Asaas, atualizada por consulta explícita. O produto
    /// cria a cobrança e não acompanha o pagamento sozinho.
    /// </summary>
    public ChargePaymentStatus PaymentStatus { get; private set; }

    public DateOnly? PaidAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Uma cobrança nestes estados não muda mais e não precisa ser consultada de novo.</summary>
    public bool IsPaymentSettled => PaymentStatus is ChargePaymentStatus.Received
        or ChargePaymentStatus.Confirmed
        or ChargePaymentStatus.Refunded;

    public static ChargeBatchItem Restore(
        Guid billingDraftId,
        ChargeBatchItemStatus status,
        string? asaasPaymentId,
        string? bankSlipUrl,
        string? errorMessage,
        ChargePaymentStatus paymentStatus,
        DateOnly? paidAt,
        DateTimeOffset updatedAt)
    {
        return new ChargeBatchItem(
            billingDraftId,
            status,
            asaasPaymentId,
            bankSlipUrl,
            errorMessage,
            paymentStatus,
            paidAt,
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

    /// <summary>
    /// Aplica a situação devolvida pelo Asaas. Um status ainda desconhecido é
    /// ignorado de propósito: sincronizar o histórico inteiro não pode parar
    /// porque o Asaas passou a devolver um estado novo.
    /// </summary>
    public void ApplyPaymentStatus(string asaasStatus, DateOnly? paidAt, DateTimeOffset updatedAt)
    {
        var mappedStatus = MapPaymentStatus(asaasStatus);
        if (mappedStatus is null)
        {
            return;
        }

        PaymentStatus = mappedStatus.Value;
        PaidAt = paidAt ?? PaidAt;
        UpdatedAt = updatedAt;
    }

    private static ChargePaymentStatus? MapPaymentStatus(string asaasStatus)
    {
        return asaasStatus switch
        {
            "PENDING" or "AWAITING_RISK_ANALYSIS" => ChargePaymentStatus.Pending,
            "RECEIVED" or "RECEIVED_IN_CASH" => ChargePaymentStatus.Received,
            "CONFIRMED" => ChargePaymentStatus.Confirmed,
            "OVERDUE" => ChargePaymentStatus.Overdue,
            "REFUNDED" or "REFUND_REQUESTED" => ChargePaymentStatus.Refunded,
            _ => null,
        };
    }
}
