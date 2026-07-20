using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/billing-periods")]
public sealed class BillingPeriodsController(BillingPeriodService billingPeriodService) : ControllerBase
{
    [HttpPost("{year:int}/{month:int}")]
    [ProducesResponseType<BillingPeriodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BillingPeriodResponse>> CreateAsync(
        int year,
        int month,
        CreateBillingPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodService.CreateAsync(
            new BillingPeriodReference(year, month),
            request.OperatorId,
            cancellationToken);

        return CreatedAtRoute(
            "GetBillingPeriod",
            new { year, month },
            BillingPeriodResponse.FromDomain(billingPeriod));
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<BillingPeriodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<BillingPeriodResponse>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var billingPeriods = await billingPeriodService.ListAsync(cancellationToken);
        return Ok(billingPeriods.Select(BillingPeriodResponse.FromDomain).ToArray());
    }

    [HttpGet("{year:int}/{month:int}", Name = "GetBillingPeriod")]
    [ProducesResponseType<BillingPeriodResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillingPeriodResponse>> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodService.GetByReferenceAsync(
            new BillingPeriodReference(year, month),
            cancellationToken);

        return Ok(BillingPeriodResponse.FromDomain(billingPeriod));
    }
}
