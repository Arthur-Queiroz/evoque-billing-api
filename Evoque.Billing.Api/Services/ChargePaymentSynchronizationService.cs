using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Atualiza junto ao Asaas a situação das cobranças ainda em aberto. Acionado
/// pela tela: o produto cria a cobrança e não acompanha o pagamento sozinho.
/// </summary>
public sealed class ChargePaymentSynchronizationService(
    IChargeBatchRepository chargeBatchRepository,
    IChargeHistoryRepository chargeHistoryRepository,
    IAsaasChargeGateway asaasChargeGateway,
    IAuditLogRepository auditLogRepository)
{
    public async Task SynchronizeAsync(string operatorId, CancellationToken cancellationToken)
    {
        var entries = await chargeHistoryRepository.ListAsync(new ChargeHistoryFilter(), cancellationToken);

        // Uma consulta por cobrança, em série, como ChargeBatchService já faz por
        // item de lote. Aceitável no volume atual; uma resposta lenta do Asaas
        // numa cobrança atrasa as demais desta mesma chamada.
        foreach (var chargeBatchId in entries.Select(entry => entry.ChargeBatchId).Distinct())
        {
            var chargeBatch = await chargeBatchRepository.FindByIdAsync(chargeBatchId, cancellationToken);
            if (chargeBatch is null)
            {
                continue;
            }

            var anyItemChanged = false;
            foreach (var chargeBatchItem in chargeBatch.Items)
            {
                if (await SynchronizeItemAsync(chargeBatch, chargeBatchItem, operatorId, cancellationToken))
                {
                    anyItemChanged = true;
                }
            }

            if (anyItemChanged)
            {
                await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Consulta uma cobrança e devolve se algo mudou nela. Uma cobrança sem
    /// identificador no Asaas nunca chegou a existir lá, e uma já assentada não
    /// muda mais — nenhuma das duas merece uma chamada externa.
    /// </summary>
    private async Task<bool> SynchronizeItemAsync(
        ChargeBatch chargeBatch,
        ChargeBatchItem chargeBatchItem,
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chargeBatchItem.AsaasPaymentId) || chargeBatchItem.IsPaymentSettled)
        {
            return false;
        }

        try
        {
            var chargeState = await asaasChargeGateway.GetChargeAsync(
                chargeBatch.AsaasEnvironment,
                chargeBatchItem.AsaasPaymentId,
                cancellationToken);

            var previousStatus = chargeBatchItem.PaymentStatus;
            var previousPaidAt = chargeBatchItem.PaidAt;

            chargeBatchItem.ApplyPaymentStatus(
                chargeState.Status,
                chargeState.PaymentDate,
                DateTimeOffset.UtcNow);

            // Persiste quando qualquer um dos dois mudou, como
            // FiscalInvoiceService.SynchronizeAsync já faz. Olhar só o status
            // descartaria a data de pagamento que chega depois, sem o status se
            // mover — o Asaas pode devolver o mesmo estado e só então preencher
            // a data.
            return chargeBatchItem.PaymentStatus != previousStatus
                || chargeBatchItem.PaidAt != previousPaidAt;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Uma indisponibilidade externa não apaga o que já sabemos nem
            // interrompe a consulta das demais cobranças.
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "charge-payment.query-failed",
                    operatorId,
                    DateTimeOffset.UtcNow,
                    chargeBatch.BillingPeriodId,
                    chargeBatchItem.BillingDraftId,
                    $"Não foi possível consultar a cobrança {chargeBatchItem.AsaasPaymentId}: {exception.Message}"),
                cancellationToken);
            return false;
        }
    }
}
