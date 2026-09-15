namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Nota fiscal de serviço emitida a partir de uma prévia aprovada que já virou
/// cobrança. A <see cref="Sequence"/> existe porque cancelamento e reemissão
/// acontecem de verdade: a produção tem notas canceladas e uma com cancelamento
/// negado pela prefeitura.
/// </summary>
public sealed class FiscalInvoice
{
    public FiscalInvoice(
        Guid billingDraftId,
        Guid billingPeriodId,
        int sequence,
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        decimal totalAmount,
        DateOnly effectiveDate,
        bool retainsIss,
        string serviceDescription,
        DateTimeOffset createdAt)
        : this(
            Guid.NewGuid(),
            billingDraftId,
            billingPeriodId,
            sequence,
            asaasEnvironment,
            asaasPaymentId,
            null,
            FiscalInvoiceStatus.Issuing,
            totalAmount,
            effectiveDate,
            retainsIss,
            serviceDescription,
            null,
            createdAt,
            createdAt)
    {
    }

    private FiscalInvoice(
        Guid id,
        Guid billingDraftId,
        Guid billingPeriodId,
        int sequence,
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        string? asaasInvoiceId,
        FiscalInvoiceStatus status,
        decimal totalAmount,
        DateOnly effectiveDate,
        bool retainsIss,
        string serviceDescription,
        string? errorMessage,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (billingDraftId == Guid.Empty)
        {
            throw new ValidationException("A prévia da nota fiscal é obrigatória.");
        }

        if (billingPeriodId == Guid.Empty)
        {
            throw new ValidationException("A competência da nota fiscal é obrigatória.");
        }

        if (sequence < 1)
        {
            throw new ValidationException("A sequência da nota fiscal começa em 1.");
        }

        if (string.IsNullOrWhiteSpace(asaasPaymentId))
        {
            throw new ValidationException("A cobrança Asaas da nota fiscal é obrigatória.");
        }

        if (totalAmount <= 0)
        {
            throw new ValidationException("O valor da nota fiscal deve ser positivo.");
        }

        if (string.IsNullOrWhiteSpace(serviceDescription))
        {
            throw new ValidationException("A descrição do serviço é obrigatória.");
        }

        Id = id;
        BillingDraftId = billingDraftId;
        BillingPeriodId = billingPeriodId;
        Sequence = sequence;
        AsaasEnvironment = asaasEnvironment;
        AsaasPaymentId = asaasPaymentId;
        AsaasInvoiceId = asaasInvoiceId;
        Status = status;
        TotalAmount = totalAmount;
        EffectiveDate = effectiveDate;
        RetainsIss = retainsIss;
        ServiceDescription = serviceDescription;
        ErrorMessage = errorMessage;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid BillingDraftId { get; }

    public Guid BillingPeriodId { get; }

    public int Sequence { get; }

    /// <summary>
    /// Ambiente em que a nota foi solicitada. Sincronização e reemissão precisam
    /// voltar ao mesmo Asaas: consultar em Produção uma nota criada no Sandbox
    /// leria a conta errada, e reemitir levaria à prefeitura uma nota de teste.
    /// </summary>
    public AsaasEnvironment AsaasEnvironment { get; }

    public string AsaasPaymentId { get; }

    public string? AsaasInvoiceId { get; private set; }

    public FiscalInvoiceStatus Status { get; private set; }

    public decimal TotalAmount { get; }

    public DateOnly EffectiveDate { get; }

    /// <summary>Retenção enviada ao Asaas, guardada para auditoria da emissão.</summary>
    public bool RetainsIss { get; }

    public string ServiceDescription { get; }

    /// <summary>Motivo devolvido pelo Asaas ou pela prefeitura, na íntegra.</summary>
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Somente uma nota recusada pode ser reemitida.</summary>
    public bool CanBeReissued => Status == FiscalInvoiceStatus.Failed;

    /// <summary>Uma nota nesses estados não muda mais e não precisa de sincronização.</summary>
    public bool IsSettled => Status is FiscalInvoiceStatus.Authorized
        or FiscalInvoiceStatus.Canceled
        or FiscalInvoiceStatus.Failed;

    public static FiscalInvoice Restore(
        Guid id,
        Guid billingDraftId,
        Guid billingPeriodId,
        int sequence,
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        string? asaasInvoiceId,
        FiscalInvoiceStatus status,
        decimal totalAmount,
        DateOnly effectiveDate,
        bool retainsIss,
        string serviceDescription,
        string? errorMessage,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new FiscalInvoice(
            id,
            billingDraftId,
            billingPeriodId,
            sequence,
            asaasEnvironment,
            asaasPaymentId,
            asaasInvoiceId,
            status,
            totalAmount,
            effectiveDate,
            retainsIss,
            serviceDescription,
            errorMessage,
            createdAt,
            updatedAt);
    }

    public void MarkScheduled(string asaasInvoiceId, DateTimeOffset updatedAt)
    {
        if (Status != FiscalInvoiceStatus.Issuing)
        {
            throw new ConflictException("Somente uma nota em emissão pode ser agendada.");
        }

        if (string.IsNullOrWhiteSpace(asaasInvoiceId))
        {
            throw new ValidationException("O identificador da nota fiscal no Asaas é obrigatório.");
        }

        AsaasInvoiceId = asaasInvoiceId;
        Status = FiscalInvoiceStatus.Scheduled;
        ErrorMessage = null;
        UpdatedAt = updatedAt;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset updatedAt)
    {
        if (Status != FiscalInvoiceStatus.Issuing)
        {
            throw new ConflictException("Somente uma nota em emissão pode ser marcada como recusada.");
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ValidationException("O motivo da recusa da nota fiscal é obrigatório.");
        }

        Status = FiscalInvoiceStatus.Failed;
        ErrorMessage = errorMessage.Trim();
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Aplica o status devolvido pelo Asaas. Um status ainda desconhecido é
    /// ignorado de propósito: sincronizar uma competência inteira não pode parar
    /// porque o Asaas passou a devolver um estado novo.
    /// </summary>
    public void ApplyAsaasStatus(string asaasStatus, string? statusDescription, DateTimeOffset updatedAt)
    {
        var mappedStatus = MapAsaasStatus(asaasStatus);
        if (mappedStatus is null)
        {
            return;
        }

        Status = mappedStatus.Value;
        ErrorMessage = string.IsNullOrWhiteSpace(statusDescription) ? null : statusDescription.Trim();
        UpdatedAt = updatedAt;
    }

    private static FiscalInvoiceStatus? MapAsaasStatus(string asaasStatus)
    {
        return asaasStatus switch
        {
            "SCHEDULED" => FiscalInvoiceStatus.Scheduled,
            "SYNCHRONIZED" => FiscalInvoiceStatus.Synchronized,
            "AUTHORIZED" => FiscalInvoiceStatus.Authorized,
            "PROCESSING_CANCELLATION" => FiscalInvoiceStatus.CancellationRequested,
            "CANCELED" => FiscalInvoiceStatus.Canceled,
            "CANCELLATION_DENIED" => FiscalInvoiceStatus.CancellationDenied,
            "ERROR" => FiscalInvoiceStatus.Failed,
            _ => null,
        };
    }
}
