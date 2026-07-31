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
public sealed class CompaniesController(
    CompanyCatalogService companyCatalogService,
    CompanyAsaasSynchronizationService companyAsaasSynchronizationService) : ControllerBase
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

    [HttpGet("{taxId}", Name = "GetCompany")]
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

        // Rota nomeada, como nos demais controllers. CreatedAtAction(nameof(GetAsync))
        // não funciona: o ASP.NET remove o sufixo "Async" do nome da action, então
        // procurava uma rota "GetAsync" inexistente e falhava ao montar a resposta —
        // depois de a empresa já ter sido gravada. O cadastro devolvia 500 com corpo
        // vazio e a empresa aparecia no catálogo mesmo assim.
        return CreatedAtRoute("GetCompany", new { taxId = company.TaxId }, company);
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

    [HttpPost("{taxId}/asaas/sandbox-sync")]
    [ProducesResponseType<CompanyAsaasSynchronizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CompanyAsaasSynchronizationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CompanyAsaasSynchronizationResponse>> SynchronizeAsaasSandboxAsync(
        string taxId,
        SynchronizeCompanyAsaasSandboxRequest request,
        CancellationToken cancellationToken)
    {
        var synchronization = await companyAsaasSynchronizationService.SynchronizeSandboxAsync(
            taxId,
            request,
            cancellationToken);
        return synchronization.CreatedNow
            ? StatusCode(StatusCodes.Status201Created, synchronization)
            : Ok(synchronization);
    }

    /// <summary>
    /// Consulta o Asaas Produção pelo CNPJ e registra localmente o vínculo
    /// encontrado. Este endpoint nunca cria nem altera um cliente no Asaas.
    /// </summary>
    [HttpPost("{taxId}/asaas/production-sync")]
    [ProducesResponseType<CompanyAsaasSynchronizationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanyAsaasSynchronizationResponse>> SynchronizeAsaasProductionAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var synchronization = await companyAsaasSynchronizationService.SynchronizeProductionAsync(
            taxId,
            request,
            cancellationToken);
        return Ok(synchronization);
    }

    [HttpGet("{taxId}/members")]
    [ProducesResponseType<IReadOnlyCollection<CorporateMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CorporateMemberResponse>>> ListMembersAsync(
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
