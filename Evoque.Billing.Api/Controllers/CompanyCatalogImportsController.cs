using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// Sincronização do catálogo pela exportação completa do CRM 2.0 do EVO. Esta
/// importação não usa valores financeiros e nunca cria prévia ou cobrança.
/// </summary>
[ApiController]
[Route("api/company-catalog-imports")]
public sealed class CompanyCatalogImportsController(
    CompanyCatalogImportService companyCatalogImportService,
    CompanyCatalogService companyCatalogService) : ControllerBase
{
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CompanyCatalogImportPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyCatalogImportPreviewResponse>> PreviewAsync(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var preview = await companyCatalogImportService.PreviewAsync(file, cancellationToken);
        return Ok(preview);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CompanyCatalogImportResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CompanyCatalogImportResponse>> SynchronizeAsync(
        [FromForm] IFormFile file,
        [FromForm] string operatorId,
        CancellationToken cancellationToken)
    {
        var result = await companyCatalogImportService.SynchronizeAsync(
            file,
            operatorId,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("latest")]
    [ProducesResponseType<CompanyCatalogImportSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<CompanyCatalogImportSummaryResponse>> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        var latestImport = await companyCatalogService.FindLatestImportAsync(cancellationToken);
        return latestImport is null ? NoContent() : Ok(latestImport);
    }
}
