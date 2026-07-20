using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController(IntegrationStatusService integrationStatusService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IntegrationStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<IntegrationStatusResponse> Get()
    {
        return Ok(integrationStatusService.GetStatus());
    }
}
