using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api/billing-comparisons")]
public sealed class BillingComparisonsController(MonthlyComparisonService monthlyComparisonService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<IReadOnlyCollection<ComparisonResultResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<ComparisonResultResponse>> Compare(
        CompareBillingSnapshotsRequest request)
    {
        var comparisonResults = monthlyComparisonService.Compare(
            request.ToPreviousCompanies(),
            request.ToCurrentCompanies());

        return Ok(comparisonResults.Select(ComparisonResultResponse.FromDomain).ToArray());
    }
}
