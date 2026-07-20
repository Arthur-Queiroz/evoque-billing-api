using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/asaas/customers")]
public sealed class AsaasCustomersController(AsaasCustomerService asaasCustomerService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AsaasCustomerListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AsaasCustomerListResponse>> ListAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var customers = await asaasCustomerService.ListAsync(
            searchTerm,
            offset,
            limit,
            cancellationToken);
        return Ok(customers);
    }
}
