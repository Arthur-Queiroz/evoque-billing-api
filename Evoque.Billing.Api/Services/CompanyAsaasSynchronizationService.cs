using System.Net.Mail;
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Resolve os vínculos entre o catálogo interno e o Asaas pelo CNPJ.
///
/// Sandbox pode criar um cliente de teste quando ele ainda não existe.
/// Produção é deliberadamente somente leitura: o serviço apenas localiza um
/// cliente já existente e persiste o vínculo interno.
/// </summary>
public sealed class CompanyAsaasSynchronizationService(
    ICompanyRepository companyRepository,
    IAsaasCustomerGateway asaasCustomerGateway,
    IAuditLogRepository auditLogRepository)
{
    public async Task<CompanyAsaasSynchronizationResponse> SynchronizeSandboxAsync(
        string taxId,
        SynchronizeCompanyAsaasSandboxRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var email = request.Email?.Trim();
        if (!MailAddress.TryCreate(email, out _))
        {
            throw new ValidationException("O e-mail controlado do cliente Sandbox é inválido.");
        }

        var lookupResult = await asaasCustomerGateway.FindByTaxIdAsync(
            AsaasEnvironment.Sandbox,
            company.TaxId,
            cancellationToken);
        if (lookupResult.Status == AsaasCustomerLookupStatus.Ambiguous)
        {
            throw new ConflictException(
                $"Foram encontrados {lookupResult.MatchCount} clientes Sandbox para o CNPJ "
                + $"{CompanyTaxId.Format(company.TaxId)}.");
        }

        var createdNow = false;
        var customer = lookupResult.Customer;
        if (customer is null)
        {
            customer = await asaasCustomerGateway.CreateSandboxAsync(
                company.DisplayName,
                company.TaxId,
                email!,
                cancellationToken);
            createdNow = true;
        }

        await LinkCustomerAsync(
            company,
            AsaasEnvironment.Sandbox,
            customer,
            request.OperatorId,
            createdNow,
            cancellationToken);
        return new CompanyAsaasSynchronizationResponse(
            AsaasEnvironment.Sandbox.ToString(),
            "Linked",
            customer.Id,
            customer.Name,
            createdNow,
            createdNow
                ? "Cliente de teste criado no Sandbox e vinculado automaticamente."
                : "Cliente de teste localizado pelo CNPJ e vinculado automaticamente.");
    }

    public async Task<CompanyAsaasSynchronizationResponse> SynchronizeProductionAsync(
        string taxId,
        CompanyOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(taxId, cancellationToken);
        var lookupResult = await asaasCustomerGateway.FindByTaxIdAsync(
            AsaasEnvironment.Production,
            company.TaxId,
            cancellationToken);

        if (lookupResult.Status == AsaasCustomerLookupStatus.NotFound)
        {
            await RegisterLookupAuditAsync(
                company,
                request.OperatorId,
                "company.asaas-production-not-found",
                "Nenhum cliente de produção foi encontrado pelo CNPJ. Nenhum dado foi criado no Asaas.",
                cancellationToken);
            return new CompanyAsaasSynchronizationResponse(
                AsaasEnvironment.Production.ToString(),
                "NotFound",
                null,
                null,
                false,
                "Nenhum cliente de produção foi encontrado para este CNPJ.");
        }

        if (lookupResult.Status == AsaasCustomerLookupStatus.Ambiguous)
        {
            await RegisterLookupAuditAsync(
                company,
                request.OperatorId,
                "company.asaas-production-ambiguous",
                $"{lookupResult.MatchCount} clientes de produção foram encontrados para o mesmo CNPJ.",
                cancellationToken);
            return new CompanyAsaasSynchronizationResponse(
                AsaasEnvironment.Production.ToString(),
                "Ambiguous",
                null,
                null,
                false,
                $"Foram encontrados {lookupResult.MatchCount} clientes de produção para o mesmo CNPJ.");
        }

        var customer = lookupResult.Customer
            ?? throw new InvalidOperationException("A consulta do Asaas retornou um resultado inconsistente.");
        await LinkCustomerAsync(
            company,
            AsaasEnvironment.Production,
            customer,
            request.OperatorId,
            createdNow: false,
            cancellationToken);
        return new CompanyAsaasSynchronizationResponse(
            AsaasEnvironment.Production.ToString(),
            "Linked",
            customer.Id,
            customer.Name,
            false,
            "Cliente de produção localizado pelo CNPJ e vinculado. Nenhum dado foi criado no Asaas.");
    }

    private async Task<Company> RequireCompanyAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        var normalizedTaxId = CompanyTaxId.Normalize(taxId);
        return await companyRepository.FindByTaxIdAsync(normalizedTaxId, cancellationToken)
            ?? throw new NotFoundException(
                $"Empresa {CompanyTaxId.Format(normalizedTaxId)} não encontrada no catálogo.");
    }

    private async Task LinkCustomerAsync(
        Company company,
        AsaasEnvironment asaasEnvironment,
        AsaasCustomer customer,
        string operatorId,
        bool createdNow,
        CancellationToken cancellationToken)
    {
        var linkedAt = DateTimeOffset.UtcNow;
        company.LinkAsaasCustomer(asaasEnvironment, customer.Id, operatorId, linkedAt);
        await companyRepository.UpsertAsync(company, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                $"company.asaas-{asaasEnvironment.ToString().ToLowerInvariant()}-linked",
                operatorId,
                linkedAt,
                null,
                null,
                $"Cliente {customer.Id} vinculado automaticamente pelo CNPJ "
                + $"{CompanyTaxId.Format(company.TaxId)}. Criado agora: {createdNow}."),
            cancellationToken);
    }

    private async Task RegisterLookupAuditAsync(
        Company company,
        string operatorId,
        string action,
        string details,
        CancellationToken cancellationToken)
    {
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                action,
                operatorId,
                DateTimeOffset.UtcNow,
                null,
                null,
                $"{CompanyTaxId.Format(company.TaxId)}: {details}"),
            cancellationToken);
    }
}
