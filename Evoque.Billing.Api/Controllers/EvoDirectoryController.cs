using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/evo")]
public sealed class EvoDirectoryController(EvoDirectoryService evoDirectoryService) : ControllerBase
{
    [HttpGet("employees")]
    [ProducesResponseType<EvoEmployeeListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EvoEmployeeListResponse>> ListEmployeesAsync(
        [FromQuery] string? name,
        [FromQuery] string? email,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var employees = await evoDirectoryService.ListEmployeesAsync(
            name,
            email,
            offset,
            limit,
            cancellationToken);
        return Ok(employees);
    }

    [HttpGet("members")]
    [ProducesResponseType<EvoMemberListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EvoMemberListResponse>> ListMembersAsync(
        [FromQuery] int status = 1,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var members = await evoDirectoryService.ListMembersAsync(
            status,
            offset,
            limit,
            cancellationToken);
        return Ok(members);
    }

    [HttpGet("companies")]
    [ProducesResponseType<EvoCompanyListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EvoCompanyListResponse>> ListCompaniesAsync(
        CancellationToken cancellationToken)
    {
        var companies = await evoDirectoryService.ListCompaniesAsync(cancellationToken);
        return Ok(companies);
    }

    [HttpGet("branch-groups")]
    [ProducesResponseType<IReadOnlyCollection<EvoBranchGroupResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<EvoBranchGroupResponse>>> ListBranchGroupsAsync(
        CancellationToken cancellationToken)
    {
        var branchGroups = await evoDirectoryService.ListBranchGroupsAsync(cancellationToken);
        return Ok(branchGroups);
    }
}
