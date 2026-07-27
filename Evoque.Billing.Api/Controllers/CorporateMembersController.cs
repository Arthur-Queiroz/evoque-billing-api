using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/corporate-members")]
public sealed class CorporateMembersController(CorporateMemberService corporateMemberService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CorporateMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CorporateMemberResponse>>> ListAsync(
        [FromQuery] ListCorporateMembersQuery query,
        CancellationToken cancellationToken)
    {
        var corporateMembers = await corporateMemberService.ListAsync(query, cancellationToken);
        return Ok(corporateMembers);
    }
}
