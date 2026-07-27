using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/evo/corporate-billing-previews")]
public sealed class CorporateBillingPreviewsController(
    CorporateBillingPreviewService corporateBillingPreviewService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CorporateBillingPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CorporateBillingPreviewResponse>> CreateAsync(
        CreateCorporateBillingPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var preview = await corporateBillingPreviewService.CreateAsync(request, cancellationToken);
        return Ok(preview);
    }
}
