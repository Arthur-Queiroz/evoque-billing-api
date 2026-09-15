using Evoque.Billing.Api.Contracts;
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
    IOptions<AsaasOptions> asaasOptions,
    TimeProvider timeProvider)
{
    /// <summary>Confirmação exigida na reemissão, como nas demais operações mutáveis de produção.</summary>
    public const string RequiredConfirmationPhrase = "CONFIRMAR";

    public async Task IssueForChargeAsync(
        Guid billingDraftId,
        string asaasPaymentId,
        string operatorId,
        AsaasEnvironment asaasEnvironment,
        CancellationToken cancellationToken)
    {
        // Quem decide é a configuração do ambiente, não o nome dele. O Sandbox
        // aceita o mesmo POST /v3/invoices e a mesma lista de serviços
        // municipais, então é onde a emissão pode ser exercitada antes de valer
        // na prefeitura. Um ambiente com a emissão desligada não tenta e não
        // deixa rastro de falha — a nota simplesmente não faz parte do lote.
        if (!asaasOptions.Value.CanIssueInvoices(asaasEnvironment))
        {
            await RegisterAuditAsync(
                "fiscal-invoice.skipped-disabled",
                operatorId,
                null,
                billingDraftId,
                $"A emissão de notas está desabilitada no ambiente {asaasEnvironment}; nenhuma nota foi solicitada.",
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
            asaasEnvironment,
            operatorId,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<FiscalInvoiceResponse>> ListByBillingPeriodAsync(
        BillingPeriodReference billingPeriodReference,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");

        var fiscalInvoices = await fiscalInvoiceRepository.ListByBillingPeriodIdAsync(
            billingPeriod.Id,
            cancellationToken);
        return fiscalInvoices.Select(FiscalInvoiceResponse.FromDomain).ToArray();
    }

    /// <summary>
    /// Atualiza no Asaas o status das notas que ainda podem mudar. É acionado pela
    /// tela: descobrir o desfecho não precisa ser automático, mas precisa existir —
    /// notas recusadas já ficaram meses sem ninguém ver.
    /// </summary>
    public async Task<IReadOnlyCollection<FiscalInvoiceResponse>> SynchronizeAsync(
        BillingPeriodReference billingPeriodReference,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");

        var fiscalInvoices = await fiscalInvoiceRepository.ListByBillingPeriodIdAsync(
            billingPeriod.Id,
            cancellationToken);

        // Uma chamada HTTP por nota, em série, dentro do mesmo request — como
        // ChargeBatchService já faz por item de lote. Aceitável no volume atual
        // (39 empresas), mas uma resposta lenta da prefeitura numa nota atrasa a
        // sincronização das demais desta mesma chamada.
        foreach (var fiscalInvoice in fiscalInvoices)
        {
            if (fiscalInvoice.IsSettled || string.IsNullOrWhiteSpace(fiscalInvoice.AsaasInvoiceId))
            {
                continue;
            }

            var invoiceState = await asaasInvoiceGateway.GetInvoiceAsync(
                fiscalInvoice.AsaasEnvironment,
                fiscalInvoice.AsaasInvoiceId,
                cancellationToken);
            var previousStatus = fiscalInvoice.Status;
            fiscalInvoice.ApplyAsaasStatus(
                invoiceState.Status,
                invoiceState.StatusDescription,
                DateTimeOffset.UtcNow);
            if (fiscalInvoice.Status == previousStatus)
            {
                continue;
            }

            await fiscalInvoiceRepository.UpdateAsync(fiscalInvoice, cancellationToken);
            await RegisterAuditAsync(
                "fiscal-invoice.status-synchronized",
                operatorId,
                fiscalInvoice.BillingPeriodId,
                fiscalInvoice.BillingDraftId,
                $"Nota fiscal {fiscalInvoice.AsaasInvoiceId} passou de {previousStatus} para {fiscalInvoice.Status}.",
                cancellationToken);
        }

        return fiscalInvoices.Select(FiscalInvoiceResponse.FromDomain).ToArray();
    }

    /// <summary>
    /// Reemite uma nota recusada pela prefeitura. Cria a sequência seguinte com a
    /// configuração vigente da empresa: é o que torna útil corrigir a retenção de
    /// ISS depois de uma recusa. Uma nota já autorizada nunca pode passar por
    /// aqui — reemiti-la duplicaria a nota na prefeitura, e o cancelamento
    /// depende dela, que já negou um pedido por competência encerrada.
    /// </summary>
    public async Task<FiscalInvoiceResponse> ReissueAsync(
        Guid fiscalInvoiceId,
        ReissueFiscalInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                request.ConfirmationPhrase?.Trim(),
                RequiredConfirmationPhrase,
                StringComparison.Ordinal))
        {
            throw new ValidationException("Digite CONFIRMAR para autorizar a reemissão da nota fiscal.");
        }

        var refusedInvoice = await fiscalInvoiceRepository.FindByIdAsync(fiscalInvoiceId, cancellationToken)
            ?? throw new NotFoundException("A nota fiscal não foi encontrada.");
        if (!refusedInvoice.CanBeReissued)
        {
            throw new ConflictException("Somente uma nota fiscal recusada pode ser reemitida.");
        }

        var billingDraft = await billingDraftRepository.FindByIdAsync(
            refusedInvoice.BillingDraftId,
            cancellationToken)
            ?? throw new NotFoundException("A prévia de faturamento não foi encontrada.");
        var existingInvoices = await fiscalInvoiceRepository.ListByBillingDraftIdAsync(
            refusedInvoice.BillingDraftId,
            cancellationToken);

        // Uma nota recusada continua recusada para sempre, inclusive depois de
        // uma reemissão bem-sucedida. Sem esta guarda, acionar a nota antiga de
        // novo emitiria uma segunda nota válida para a mesma prévia — e a
        // prefeitura já negou cancelamento por competência encerrada.
        if (existingInvoices.Any(existingInvoice =>
                existingInvoice.Id != refusedInvoice.Id && !existingInvoice.CanBeReissued))
        {
            throw new ConflictException(
                "Esta prévia já possui uma nota fiscal mais recente que não está recusada.");
        }

        // A reemissão volta ao mesmo ambiente da nota recusada. Reemitir em
        // Produção uma nota criada no Sandbox mandaria um teste à prefeitura.
        var reissuedInvoice = await IssueAsync(
            billingDraft,
            refusedInvoice.AsaasPaymentId,
            existingInvoices.Count + 1,
            refusedInvoice.AsaasEnvironment,
            request.OperatorId,
            cancellationToken);
        return FiscalInvoiceResponse.FromDomain(reissuedInvoice);
    }

    private async Task<FiscalInvoice> IssueAsync(
        BillingDraft billingDraft,
        string asaasPaymentId,
        int sequence,
        AsaasEnvironment asaasEnvironment,
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
            asaasEnvironment,
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
                asaasEnvironment,
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
