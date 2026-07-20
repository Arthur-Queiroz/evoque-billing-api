using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/company-billing-schedules")]
public sealed class CompanyBillingSchedulesController(CompanyBillingScheduleService companyBillingScheduleService)
    : ControllerBase
{
    [HttpPut("{externalCompanyId}")]
    [ProducesResponseType<CompanyBillingScheduleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyBillingScheduleResponse>> UpsertAsync(
        string externalCompanyId,
        UpsertCompanyBillingScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var companyBillingSchedule = await companyBillingScheduleService.UpsertAsync(
            externalCompanyId,
            request,
            cancellationToken);
        return Ok(companyBillingSchedule);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CompanyBillingScheduleResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CompanyBillingScheduleResponse>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var companyBillingSchedules = await companyBillingScheduleService.ListAsync(cancellationToken);
        return Ok(companyBillingSchedules);
    }
}
