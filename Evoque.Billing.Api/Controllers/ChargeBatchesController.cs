using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/charge-batches")]
public sealed class ChargeBatchesController(
    ChargeBatchService chargeBatchService,
    ScheduledChargeBatchService scheduledChargeBatchService) : ControllerBase
{
    [HttpPost("previews")]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ChargeBatchResponse>> CreatePreviewAsync(
        CreateChargeBatchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await chargeBatchService.CreatePreviewAsync(request, cancellationToken);
        return Created($"/api/charge-batches/{chargeBatch.Id}", chargeBatch);
    }

    [HttpPost("/api/billing-periods/{year:int}/{month:int}/scheduled-charge-batches/previews")]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ChargeBatchResponse>> CreateScheduledPreviewAsync(
        int year,
        int month,
        CreateScheduledChargeBatchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await scheduledChargeBatchService.CreatePreviewAsync(
            new BillingPeriodReference(year, month),
            request,
            cancellationToken);
        return Created($"/api/charge-batches/{chargeBatch.Id}", chargeBatch);
    }

    [HttpPost]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChargeBatchResponse>> CreateAsync(
        CreateChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await chargeBatchService.CreateAsync(request, cancellationToken);
        return Ok(chargeBatch);
    }

    [HttpPost("{chargeBatchId:guid}/approve")]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChargeBatchResponse>> ApproveAsync(
        Guid chargeBatchId,
        ApproveChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await chargeBatchService.ApproveAsync(chargeBatchId, request, cancellationToken);
        return Ok(chargeBatch);
    }

    [HttpPost("{chargeBatchId:guid}/execute")]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChargeBatchResponse>> ExecuteAsync(
        Guid chargeBatchId,
        ExecuteChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await chargeBatchService.ExecuteAsync(chargeBatchId, request, cancellationToken);
        return Ok(chargeBatch);
    }

    [HttpPost("{chargeBatchId:guid}/retry-failed")]
    [ProducesResponseType<ChargeBatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChargeBatchResponse>> RetryFailedAsync(
        Guid chargeBatchId,
        RetryFailedChargeBatchRequest request,
        CancellationToken cancellationToken)
    {
        var chargeBatch = await chargeBatchService.RetryFailedAsync(
            chargeBatchId,
            request,
            cancellationToken);
        return Ok(chargeBatch);
    }

    [HttpGet("/api/billing-periods/{year:int}/{month:int}/charge-batches")]
    [ProducesResponseType<IReadOnlyCollection<ChargeBatchResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ChargeBatchResponse>>> ListAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var chargeBatches = await chargeBatchService.ListByBillingPeriodAsync(
            new BillingPeriodReference(year, month),
            cancellationToken);
        return Ok(chargeBatches);
    }
}
