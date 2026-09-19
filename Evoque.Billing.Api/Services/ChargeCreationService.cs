using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class ChargeCreationService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    ICompanyRepository companyRepository,
    IAuditLogRepository auditLogRepository,
    IAsaasCustomerNotificationGateway asaasCustomerNotificationGateway,
    IAsaasChargeGateway asaasChargeGateway,
    TimeProvider timeProvider)
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

        var currentDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        if (dueDate < currentDate)
        {
            throw new ValidationException(
                $"O vencimento {dueDate:dd/MM/yyyy} já passou. Selecione uma competência com vencimento a partir de {currentDate:dd/MM/yyyy}.");
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

        // O cliente vem do catálogo, no ambiente deste lote, e não do que a
        // prévia guardou. O identificador pertence a um ambiente: o do Sandbox
        // não existe na conta de Produção, e a prévia é criada sem saber em qual
        // lote vai ser executada.
        //
        // `BillingDraft.AsaasCustomerId` ainda existe e é gravado, mas não
        // decide mais nada aqui. Remover o campo é trabalho à parte.
        var company = await companyRepository.FindByTaxIdAsync(billingDraft.CompanyTaxId, cancellationToken)
            ?? throw new NotFoundException(
                $"A empresa {CompanyTaxId.Format(billingDraft.CompanyTaxId)} não está no catálogo.");

        var asaasCustomerId = company.AsaasCustomerIdFor(asaasEnvironment);
        if (string.IsNullOrWhiteSpace(asaasCustomerId))
        {
            throw new ValidationException(
                $"A empresa {company.DisplayName} não tem cliente Asaas sincronizado no ambiente "
                + $"{asaasEnvironment}. Sincronize o cliente antes de criar a cobrança.");
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
            asaasCustomerId,
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
                asaasCustomerId,
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

        return new ChargeCreationResult(asaasCharge.PaymentId, asaasCharge.BankSlipUrl, true);
    }
}

public sealed record ChargeCreationResult(string AsaasPaymentId, string? BankSlipUrl, bool CreatedNow);
