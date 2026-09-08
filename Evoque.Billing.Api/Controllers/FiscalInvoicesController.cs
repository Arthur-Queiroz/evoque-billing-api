using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// Notas fiscais emitidas a partir das cobranças criadas pelo software. A
/// emissão acontece no lote; aqui ficam a consulta, a atualização de status e a
/// reemissão de uma nota recusada pela prefeitura.
/// </summary>
[ApiController]
[Route("api/fiscal-invoices")]
public sealed class FiscalInvoicesController(FiscalInvoiceService fiscalInvoiceService) : ControllerBase
{
    [HttpGet("/api/billing-periods/{year:int}/{month:int}/fiscal-invoices")]
    [ProducesResponseType<IReadOnlyCollection<FiscalInvoiceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<FiscalInvoiceResponse>>> ListAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = await fiscalInvoiceService.ListByBillingPeriodAsync(
            new BillingPeriodReference(year, month),
            cancellationToken);
        return Ok(fiscalInvoices);
    }

    [HttpPost("/api/billing-periods/{year:int}/{month:int}/fiscal-invoices/synchronize")]
    [ProducesResponseType<IReadOnlyCollection<FiscalInvoiceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<FiscalInvoiceResponse>>> SynchronizeAsync(
        int year,
        int month,
        SynchronizeFiscalInvoicesRequest request,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = await fiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(year, month),
            request.OperatorId,
            cancellationToken);
        return Ok(fiscalInvoices);
    }

    [HttpPost("{fiscalInvoiceId:guid}/reissue")]
    [ProducesResponseType<FiscalInvoiceResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FiscalInvoiceResponse>> ReissueAsync(
        Guid fiscalInvoiceId,
        ReissueFiscalInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var fiscalInvoice = await fiscalInvoiceService.ReissueAsync(
            fiscalInvoiceId,
            request,
            cancellationToken);
        return Ok(fiscalInvoice);
    }
}
