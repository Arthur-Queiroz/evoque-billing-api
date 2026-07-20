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
