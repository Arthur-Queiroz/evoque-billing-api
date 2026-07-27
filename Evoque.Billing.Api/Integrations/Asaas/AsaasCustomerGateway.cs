using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Asaas;

public sealed class AsaasCustomerGateway(
    HttpClient httpClient,
    IHostEnvironment hostEnvironment,
    IOptions<AsaasOptions> asaasOptions) : IAsaasCustomerGateway
{
    public async Task<AsaasCustomerPage> ListAsync(
        string? searchTerm,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(AsaasEnvironment.Sandbox);
        AsaasOperationPolicy.ValidateReadOperation(
            hostEnvironment,
            AsaasEnvironment.Sandbox,
            connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        var queryParts = new List<string>
        {
            $"offset={offset}",
            $"limit={limit}",
        };
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            queryParts.Add($"name={Uri.EscapeDataString(searchTerm.Trim())}");
        }

        using var response = await httpClient.GetAsync(
            $"customers?{string.Join("&", queryParts)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                "Não foi possível consultar os clientes no Asaas.");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasCustomerListResponse>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Asaas retornou uma resposta inválida ao consultar clientes.");

        return new AsaasCustomerPage(
            responseData.Data.Select(customer => new AsaasCustomer(
                customer.Id,
                customer.Name,
                customer.CpfCnpj,
                customer.Email,
                customer.AdditionalEmails)).ToArray(),
            responseData.HasMore,
            responseData.TotalCount);
    }

    public async Task<AsaasCustomerLookupResult> FindByTaxIdAsync(
        AsaasEnvironment asaasEnvironment,
        string taxId,
        CancellationToken cancellationToken)
    {
        var responseData = await ListByEnvironmentAsync(
            asaasEnvironment,
            $"cpfCnpj={Uri.EscapeDataString(taxId)}&offset=0&limit=2",
            cancellationToken);
        var matchingCustomers = responseData.Data
            .Where(customer => NormalizeTaxId(customer.CpfCnpj) == taxId)
            .Select(ToDomain)
            .ToArray();

        return matchingCustomers.Length switch
        {
            0 => AsaasCustomerLookupResult.NotFound(),
            1 => AsaasCustomerLookupResult.Found(matchingCustomers[0]),
            _ => AsaasCustomerLookupResult.Ambiguous(matchingCustomers.Length),
        };
    }

    public async Task<AsaasCustomer> CreateSandboxAsync(
        string name,
        string taxId,
        string email,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(AsaasEnvironment.Sandbox);
        AsaasOperationPolicy.ValidateChargeCreation(
            hostEnvironment,
            AsaasEnvironment.Sandbox,
            connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        using var response = await httpClient.PostAsJsonAsync(
            "customers",
            new
            {
                name,
                cpfCnpj = taxId,
                email,
                notificationDisabled = false,
            },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"O Asaas Sandbox recusou a criação do cliente com HTTP {(int)response.StatusCode}.");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasCustomerResponse>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Asaas Sandbox retornou uma resposta inválida ao criar o cliente.");
        return ToDomain(responseData);
    }

    private async Task<AsaasCustomerListResponse> ListByEnvironmentAsync(
        AsaasEnvironment asaasEnvironment,
        string queryString,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateReadOperation(
            hostEnvironment,
            asaasEnvironment,
            connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        using var response = await httpClient.GetAsync($"customers?{queryString}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar o cliente no Asaas {asaasEnvironment}.");
        }

        return await response.Content.ReadFromJsonAsync<AsaasCustomerListResponse>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                $"O Asaas {asaasEnvironment} retornou uma resposta inválida ao consultar clientes.");
    }

    private static AsaasCustomer ToDomain(AsaasCustomerResponse customer)
    {
        return new AsaasCustomer(
            customer.Id,
            customer.Name,
            customer.CpfCnpj,
            customer.Email,
            customer.AdditionalEmails);
    }

    private static string NormalizeTaxId(string? taxId)
    {
        return new string((taxId ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
    }

    private sealed record AsaasCustomerListResponse(
        IReadOnlyCollection<AsaasCustomerResponse> Data,
        bool HasMore,
        int TotalCount);

    private sealed record AsaasCustomerResponse(
        string Id,
        string Name,
        string? CpfCnpj,
        string? Email,
        string? AdditionalEmails);
}
