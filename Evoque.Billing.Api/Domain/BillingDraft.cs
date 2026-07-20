namespace Evoque.Billing.Api.Domain;

public sealed class BillingDraft
{
    private readonly List<BillingDraftItem> items;

    public BillingDraft(
        Guid billingPeriodId,
        string externalCompanyId,
        string companyName,
        string companyTaxId,
        string? asaasCustomerId,
        IReadOnlyCollection<BillingDraftItem> items,
        DateTimeOffset createdAt)
        : this(
            Guid.NewGuid(),
            billingPeriodId,
            externalCompanyId,
            companyName,
            companyTaxId,
            asaasCustomerId,
            items,
            BillingDraftStatus.PendingReview,
            1,
            null,
            null,
            null,
            null,
            createdAt,
            createdAt)
    {
    }

    private BillingDraft(
        Guid id,
        Guid billingPeriodId,
        string externalCompanyId,
        string companyName,
        string companyTaxId,
        string? asaasCustomerId,
        IReadOnlyCollection<BillingDraftItem> items,
        BillingDraftStatus status,
        int version,
        string? approvedBy,
        DateTimeOffset? approvedAt,
        string? asaasPaymentId,
        string? bankSlipUrl,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(externalCompanyId))
        {
            throw new ValidationException("O identificador externo da empresa é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ValidationException("O nome da empresa é obrigatório.");
        }

        if (items.Count == 0)
        {
            throw new ValidationException("Uma prévia precisa ter pelo menos um item.");
        }

        Id = id;
        BillingPeriodId = billingPeriodId;
        ExternalCompanyId = externalCompanyId;
        CompanyName = companyName;
        CompanyTaxId = companyTaxId;
        AsaasCustomerId = asaasCustomerId;
        this.items = items.ToList();
        Status = status;
        Version = version;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        AsaasPaymentId = asaasPaymentId;
        BankSlipUrl = bankSlipUrl;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public Guid BillingPeriodId { get; }

    public string ExternalCompanyId { get; }

    public string CompanyName { get; }

    public string CompanyTaxId { get; }

    public string? AsaasCustomerId { get; }

    public IReadOnlyCollection<BillingDraftItem> Items => items;

    public decimal TotalAmount => items.Sum(item => item.TotalAmount);

    public BillingDraftStatus Status { get; private set; }

    public int Version { get; private set; } = 1;

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? AsaasPaymentId { get; private set; }

    public string? BankSlipUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static BillingDraft Restore(
        Guid id,
        Guid billingPeriodId,
        string externalCompanyId,
        string companyName,
        string companyTaxId,
        string? asaasCustomerId,
        IReadOnlyCollection<BillingDraftItem> items,
        BillingDraftStatus status,
        int version,
        string? approvedBy,
        DateTimeOffset? approvedAt,
        string? asaasPaymentId,
        string? bankSlipUrl,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new BillingDraft(
            id,
            billingPeriodId,
            externalCompanyId,
            companyName,
            companyTaxId,
            asaasCustomerId,
            items,
            status,
            version,
            approvedBy,
            approvedAt,
            asaasPaymentId,
            bankSlipUrl,
            createdAt,
            updatedAt);
    }

    public void Approve(string operatorId, DateTimeOffset approvedAt)
    {
        if (Status != BillingDraftStatus.PendingReview)
        {
            throw new ConflictException("Somente prévias pendentes de revisão podem ser aprovadas.");
        }

        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new ValidationException("O responsável pela aprovação é obrigatório.");
        }

        Status = BillingDraftStatus.Approved;
        ApprovedBy = operatorId;
        ApprovedAt = approvedAt;
        UpdatedAt = approvedAt;
    }

    public void MarkChargeCreated(string asaasPaymentId, string? bankSlipUrl, DateTimeOffset updatedAt)
    {
        if (Status != BillingDraftStatus.Approved)
        {
            throw new ConflictException("A prévia precisa estar aprovada antes de criar uma cobrança.");
        }

        if (string.IsNullOrWhiteSpace(asaasPaymentId))
        {
            throw new ValidationException("O identificador da cobrança no Asaas é obrigatório.");
        }

        Status = BillingDraftStatus.ChargeCreated;
        AsaasPaymentId = asaasPaymentId;
        BankSlipUrl = bankSlipUrl;
        UpdatedAt = updatedAt;
    }
}
