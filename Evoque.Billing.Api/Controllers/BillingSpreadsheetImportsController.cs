using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/billing-periods/{year:int}/{month:int}/spreadsheet-imports")]
public sealed class BillingSpreadsheetImportsController(
    BillingSpreadsheetImportService billingSpreadsheetImportService) : ControllerBase
{
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<BillingSpreadsheetPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillingSpreadsheetPreviewResponse>> PreviewAsync(
        int year,
        int month,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        _ = new BillingPeriodReference(year, month);
        var preview = await billingSpreadsheetImportService.PreviewAsync(file, cancellationToken);
        return Ok(preview);
    }

    [HttpPost("drafts")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<BillingSpreadsheetDraftImportResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BillingSpreadsheetDraftImportResponse>> CreateDraftsAsync(
        int year,
        int month,
        [FromForm] IFormFile file,
        [FromForm] string operatorId,
        [FromForm] string? asaasCustomerId,
        CancellationToken cancellationToken)
    {
        var result = await billingSpreadsheetImportService.CreateDraftsAsync(
            new BillingPeriodReference(year, month),
            file,
            operatorId,
            asaasCustomerId,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
