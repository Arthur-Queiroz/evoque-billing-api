using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// Catálogo interno de empresas pagadoras. Não existe exclusão física: uma
/// empresa que deixa o corporativo é inativada, preservando prévias, lotes e
/// auditoria.
/// </summary>
[ApiController]
[Route("api/companies")]
public sealed class CompaniesController(CompanyCatalogService companyCatalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CompanyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CompanyResponse>>> ListAsync(
        [FromQuery] ListCompaniesQuery query,
        CancellationToken cancellationToken)
    {
        var companies = await companyCatalogService.ListAsync(query, cancellationToken);
        return Ok(companies);
    }

    [HttpGet("{taxId}")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyResponse>> GetAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.GetAsync(taxId, cancellationToken);
        return Ok(company);
    }

    [HttpPost]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CompanyResponse>> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetAsync), new { taxId = company.TaxId }, company);
    }

    [HttpPut("{taxId}")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyResponse>> UpdateAsync(
        string taxId,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.UpdateAsync(taxId, request, cancellationToken);
        return Ok(company);
    }

    [HttpPost("{taxId}/deactivate")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyResponse>> DeactivateAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.DeactivateAsync(taxId, request, cancellationToken);
        return Ok(company);
    }

    [HttpPost("{taxId}/reactivate")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyResponse>> ReactivateAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.ReactivateAsync(taxId, request, cancellationToken);
        return Ok(company);
    }

    [HttpPost("{taxId}/registry-refresh")]
    [ProducesResponseType<CompanyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyResponse>> RefreshRegistryAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await companyCatalogService.RefreshRegistryAsync(taxId, request, cancellationToken);
        return Ok(company);
    }

    [HttpGet("{taxId}/members")]
    [ProducesResponseType<IReadOnlyCollection<CompanyMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CompanyMemberResponse>>> ListMembersAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        var members = await companyCatalogService.ListMembersAsync(taxId, cancellationToken);
        return Ok(members);
    }

    [HttpGet("{taxId}/billing-history")]
    [ProducesResponseType<IReadOnlyCollection<CompanyBillingHistoryEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CompanyBillingHistoryEntryResponse>>>
        ListBillingHistoryAsync(string taxId, CancellationToken cancellationToken)
    {
        var billingHistory = await companyCatalogService.ListBillingHistoryAsync(taxId, cancellationToken);
        return Ok(billingHistory);
    }
}
