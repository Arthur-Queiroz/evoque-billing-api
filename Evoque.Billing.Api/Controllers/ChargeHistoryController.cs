using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// O que este sistema emitiu, em ordem cronológica. Não inclui cobranças
/// criadas diretamente no painel do Asaas: elas não têm competência nem prévia
/// deste lado.
/// </summary>
[ApiController]
[Route("api/charge-history")]
public sealed class ChargeHistoryController(
    ChargeHistoryService chargeHistoryService,
    ChargePaymentSynchronizationService chargePaymentSynchronizationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ChargeHistoryEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ChargeHistoryEntryResponse>>> ListAsync(
        [FromQuery] ChargeHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var historico = await chargeHistoryService.ListAsync(query, cancellationToken);
        return Ok(historico);
    }

    /// <summary>
    /// Pergunta ao Asaas o que houve com as cobranças ainda em aberto e devolve
    /// o histórico já atualizado. Só lê: nenhuma cobrança é criada, alterada ou
    /// cancelada por aqui.
    /// </summary>
    [HttpPost("synchronize")]
    [ProducesResponseType<IReadOnlyCollection<ChargeHistoryEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ChargeHistoryEntryResponse>>> SynchronizeAsync(
        SynchronizeChargeHistoryRequest request,
        CancellationToken cancellationToken)
    {
        await chargePaymentSynchronizationService.SynchronizeAsync(request.OperatorId, cancellationToken);
        var historico = await chargeHistoryService.ListAsync(new ChargeHistoryQuery(), cancellationToken);
        return Ok(historico);
    }
}
