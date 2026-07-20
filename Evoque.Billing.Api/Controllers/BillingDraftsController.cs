using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class BillingDraftsController(
    BillingDraftService billingDraftService,
    ChargeCreationService chargeCreationService) : ControllerBase
{
    [HttpPost("billing-periods/{year:int}/{month:int}/drafts")]
    [ProducesResponseType<BillingDraftResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BillingDraftResponse>> CreateAsync(
        int year,
        int month,
        CreateBillingDraftRequest request,
        CancellationToken cancellationToken)
    {
        var billingDraft = await billingDraftService.CreateAsync(
            new BillingPeriodReference(year, month),
            request.ToCommand(),
            request.OperatorId,
            cancellationToken);

        return CreatedAtRoute(
            "GetBillingDraft",
            new { billingDraftId = billingDraft.Id },
            BillingDraftResponse.FromDomain(billingDraft));
    }

    [HttpGet("billing-periods/{year:int}/{month:int}/drafts")]
    [ProducesResponseType<IReadOnlyCollection<BillingDraftResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<BillingDraftResponse>>> ListAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var billingDrafts = await billingDraftService.ListAsync(
            new BillingPeriodReference(year, month),
            cancellationToken);

        return Ok(billingDrafts.Select(BillingDraftResponse.FromDomain).ToArray());
    }

    [HttpGet("billing-drafts/{billingDraftId:guid}", Name = "GetBillingDraft")]
    [ProducesResponseType<BillingDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillingDraftResponse>> GetAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        var billingDraft = await billingDraftService.GetByIdAsync(billingDraftId, cancellationToken);
        return Ok(BillingDraftResponse.FromDomain(billingDraft));
    }

    [HttpPost("billing-drafts/{billingDraftId:guid}/approve")]
    [ProducesResponseType<BillingDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BillingDraftResponse>> ApproveAsync(
        Guid billingDraftId,
        ApproveBillingDraftRequest request,
        CancellationToken cancellationToken)
    {
        var billingDraft = await billingDraftService.ApproveAsync(
            billingDraftId,
            request.OperatorId,
            cancellationToken);

        return Ok(BillingDraftResponse.FromDomain(billingDraft));
    }

    [HttpPost("billing-drafts/{billingDraftId:guid}/charges")]
    public async Task<ActionResult> CreateChargeAsync(
        Guid billingDraftId,
        CreateChargeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await chargeCreationService.CreateAsync(
            billingDraftId,
            request.DueDate,
            request.OperatorId,
            request.ConfirmationPhrase,
            AsaasEnvironment.Sandbox,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("billing-drafts/{billingDraftId:guid}/audit-logs")]
    [ProducesResponseType<IReadOnlyCollection<AuditLogResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<AuditLogResponse>>> ListAuditLogsAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        var auditLogs = await billingDraftService.ListAuditLogsAsync(billingDraftId, cancellationToken);
        return Ok(auditLogs.Select(AuditLogResponse.FromDomain).ToArray());
    }
}
