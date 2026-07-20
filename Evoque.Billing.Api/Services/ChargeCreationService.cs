using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class ChargeCreationService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    IAuditLogRepository auditLogRepository,
    IAsaasCustomerNotificationGateway asaasCustomerNotificationGateway,
    IAsaasChargeGateway asaasChargeGateway)
{
    public const string RequiredConfirmationPhrase = "CONFIRMAR";

    public async Task<ChargeCreationResult> CreateAsync(
        Guid billingDraftId,
        DateOnly dueDate,
        string operatorId,
        string confirmationPhrase,
        AsaasEnvironment asaasEnvironment,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmationPhrase?.Trim(), RequiredConfirmationPhrase, StringComparison.Ordinal))
        {
            throw new ValidationException("Digite CONFIRMAR para autorizar a criação da cobrança.");
        }

        var billingDraft = await billingDraftRepository.FindByIdAsync(billingDraftId, cancellationToken)
            ?? throw new NotFoundException("A prévia de faturamento não foi encontrada.");

        if (asaasEnvironment == AsaasEnvironment.Production
            && billingDraft.Status == BillingDraftStatus.ChargeCreated)
        {
            return new ChargeCreationResult(billingDraft.AsaasPaymentId!, billingDraft.BankSlipUrl, false);
        }

        if (billingDraft.Status != BillingDraftStatus.Approved)
        {
            throw new ConflictException("A prévia precisa estar aprovada antes de criar uma cobrança.");
        }

        if (string.IsNullOrWhiteSpace(billingDraft.AsaasCustomerId))
        {
            throw new ValidationException("A empresa não possui identificador de cliente no Asaas.");
        }

        var billingPeriod = (await billingPeriodRepository.ListAsync(cancellationToken))
            .SingleOrDefault(currentBillingPeriod => currentBillingPeriod.Id == billingDraft.BillingPeriodId)
            ?? throw new NotFoundException("A competência da prévia não foi encontrada.");

        if (billingPeriod.Status != BillingPeriodStatus.Approved)
        {
            throw new ConflictException("A competência precisa estar aprovada antes de criar cobranças.");
        }

        var emailDeliveryReadiness = await asaasCustomerNotificationGateway.GetEmailDeliveryReadinessAsync(
            asaasEnvironment,
            billingDraft.AsaasCustomerId,
            cancellationToken);
        if (!emailDeliveryReadiness.HasEmailRecipient)
        {
            throw new ValidationException("O cliente Asaas não possui um e-mail para receber a cobrança.");
        }

        if (!emailDeliveryReadiness.PaymentCreatedEmailEnabled)
        {
            throw new ConflictException(
                "O aviso de cobrança criada por e-mail está desabilitado para este cliente Asaas.");
        }

        var asaasCharge = await asaasChargeGateway.CreateChargeAsync(
            asaasEnvironment,
            new AsaasChargeRequest(
                billingDraft.AsaasCustomerId,
                billingDraft.TotalAmount,
                dueDate,
                $"Faturamento Evoque {billingPeriod.Reference} - {billingDraft.CompanyName}",
                $"billing-draft:{billingDraft.Id}:version:{billingDraft.Version}:environment:{asaasEnvironment.ToString().ToLowerInvariant()}"),
            cancellationToken);

        var updatedAt = DateTimeOffset.UtcNow;
        if (asaasEnvironment == AsaasEnvironment.Production)
        {
            billingDraft.MarkChargeCreated(asaasCharge.PaymentId, asaasCharge.BankSlipUrl, updatedAt);
            await billingDraftRepository.UpdateAsync(billingDraft, cancellationToken);
        }

        await auditLogRepository.AddAsync(
            AuditLog.Create(
                asaasEnvironment == AsaasEnvironment.Production
                    ? "asaas-charge.created"
                    : "asaas-charge.sandbox-created",
                operatorId,
                updatedAt,
                billingPeriod.Id,
                billingDraft.Id,
                $"Cobrança Asaas {asaasCharge.PaymentId} criada com vencimento em {dueDate:yyyy-MM-dd}."),
            cancellationToken);

        var billingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriod.Id, cancellationToken);
        if (asaasEnvironment == AsaasEnvironment.Production
            && billingDrafts.All(currentBillingDraft => currentBillingDraft.Status == BillingDraftStatus.ChargeCreated))
        {
            billingPeriod.MarkChargesCreated(updatedAt);
            await billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);
        }

        return new ChargeCreationResult(asaasCharge.PaymentId, asaasCharge.BankSlipUrl, true);
    }
}

public sealed record ChargeCreationResult(string AsaasPaymentId, string? BankSlipUrl, bool CreatedNow);
