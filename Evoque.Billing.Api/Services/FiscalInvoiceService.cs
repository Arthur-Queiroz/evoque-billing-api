using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Emite a NFS-e de uma prévia que acabou de virar cobrança, no mesmo lote
/// autorizado. O gatilho reproduz a prática já existente na conta: a nota sai
/// junto com o boleto, sem esperar o pagamento.
/// </summary>
public sealed class FiscalInvoiceService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    IFiscalInvoiceRepository fiscalInvoiceRepository,
    ICompanyRepository companyRepository,
    IAuditLogRepository auditLogRepository,
    IAsaasInvoiceGateway asaasInvoiceGateway,
    IOptions<FiscalInvoiceOptions> fiscalInvoiceOptions,
    TimeProvider timeProvider)
{
    public async Task IssueForChargeAsync(
        Guid billingDraftId,
        string asaasPaymentId,
        string operatorId,
        AsaasEnvironment asaasEnvironment,
        CancellationToken cancellationToken)
    {
        if (asaasEnvironment != AsaasEnvironment.Production)
        {
            await RegisterAuditAsync(
                "fiscal-invoice.skipped-sandbox",
                operatorId,
                null,
                billingDraftId,
                "O Sandbox não emite NFS-e; nenhuma nota foi solicitada.",
                cancellationToken);
            return;
        }

        var billingDraft = await billingDraftRepository.FindByIdAsync(billingDraftId, cancellationToken)
            ?? throw new NotFoundException("A prévia de faturamento não foi encontrada.");

        var existingInvoices = await fiscalInvoiceRepository.ListByBillingDraftIdAsync(
            billingDraftId,
            cancellationToken);
        if (existingInvoices.Any(fiscalInvoice => !fiscalInvoice.CanBeReissued))
        {
            return;
        }

        await IssueAsync(
            billingDraft,
            asaasPaymentId,
            existingInvoices.Count + 1,
            operatorId,
            cancellationToken);
    }

    private async Task<FiscalInvoice> IssueAsync(
        BillingDraft billingDraft,
        string asaasPaymentId,
        int sequence,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingPeriod = (await billingPeriodRepository.ListAsync(cancellationToken))
            .SingleOrDefault(currentBillingPeriod => currentBillingPeriod.Id == billingDraft.BillingPeriodId)
            ?? throw new NotFoundException("A competência da prévia não foi encontrada.");

        var company = await companyRepository.FindByTaxIdAsync(
            billingDraft.ExternalCompanyId,
            cancellationToken);
        var retainsIss = company?.RetainsIss ?? false;
        var options = fiscalInvoiceOptions.Value;
        var effectiveDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var serviceDescription = options.BuildServiceDescription(billingPeriod.Reference);

        // O registro nasce antes da chamada externa: com a chave única por prévia
        // e sequência, dois lotes concorrentes não emitem duas notas.
        var fiscalInvoice = new FiscalInvoice(
            billingDraft.Id,
            billingDraft.BillingPeriodId,
            sequence,
            asaasPaymentId,
            billingDraft.TotalAmount,
            effectiveDate,
            retainsIss,
            serviceDescription,
            DateTimeOffset.UtcNow);
        await fiscalInvoiceRepository.AddAsync(fiscalInvoice, cancellationToken);

        // Montado fora do try: um erro de programação aqui (por exemplo um
        // NullReferenceException) não pode ser confundido com uma recusa do
        // Asaas. O try abaixo cobre só a chamada externa e a interpretação do
        // resultado dela.
        var invoiceRequest = new AsaasInvoiceRequest
        {
            AsaasPaymentId = asaasPaymentId,
            Value = billingDraft.TotalAmount,
            EffectiveDate = effectiveDate,
            ServiceDescription = serviceDescription,
            MunicipalServiceId = options.MunicipalServiceId,
            MunicipalServiceName = options.BuildMunicipalServiceName(billingPeriod.Reference),
            ExternalReference = $"billing-draft:{billingDraft.Id}:version:{billingDraft.Version}",
            RetainsIss = retainsIss,
            IssTaxRate = options.IssTaxRate,
        };

        try
        {
            var invoiceCreation = await asaasInvoiceGateway.ScheduleInvoiceAsync(
                AsaasEnvironment.Production,
                invoiceRequest,
                cancellationToken);
            fiscalInvoice.MarkScheduled(invoiceCreation.InvoiceId, DateTimeOffset.UtcNow);
            await fiscalInvoiceRepository.UpdateAsync(fiscalInvoice, cancellationToken);
            await RegisterAuditAsync(
                "fiscal-invoice.scheduled",
                operatorId,
                billingDraft.BillingPeriodId,
                billingDraft.Id,
                $"Nota fiscal {invoiceCreation.InvoiceId} agendada para {effectiveDate:yyyy-MM-dd}.",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            fiscalInvoice.MarkFailed(GetFailureMessage(exception), DateTimeOffset.UtcNow);
            await fiscalInvoiceRepository.UpdateAsync(fiscalInvoice, cancellationToken);
            await RegisterAuditAsync(
                "fiscal-invoice.failed",
                operatorId,
                billingDraft.BillingPeriodId,
                billingDraft.Id,
                $"A emissão da nota fiscal falhou: {fiscalInvoice.ErrorMessage}",
                cancellationToken);
        }

        return fiscalInvoice;
    }

    private async Task RegisterAuditAsync(
        string action,
        string operatorId,
        Guid? billingPeriodId,
        Guid? billingDraftId,
        string details,
        CancellationToken cancellationToken)
    {
        await auditLogRepository.AddAsync(
            AuditLog.Create(action, operatorId, DateTimeOffset.UtcNow, billingPeriodId, billingDraftId, details),
            cancellationToken);
    }

    private static string GetFailureMessage(Exception exception)
    {
        return exception is DomainException
            ? exception.Message
            : "Não foi possível emitir a nota fiscal. Tente novamente ou consulte o suporte técnico.";
    }
}
