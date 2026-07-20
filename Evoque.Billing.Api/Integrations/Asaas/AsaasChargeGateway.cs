using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Asaas;

public sealed class AsaasChargeGateway(
    HttpClient httpClient,
    IHostEnvironment hostEnvironment,
    IOptions<AsaasOptions> asaasOptions) : IAsaasChargeGateway
{
    public async Task<AsaasChargeCreation> CreateChargeAsync(
        AsaasEnvironment asaasEnvironment,
        AsaasChargeRequest request,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateChargeCreation(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        var requestBody = new
        {
            customer = request.CustomerId,
            billingType = "BOLETO",
            value = request.Amount,
            dueDate = request.DueDate.ToString("yyyy-MM-dd"),
            description = request.Description,
            externalReference = request.ExternalReference,
        };

        using var response = await httpClient.PostAsJsonAsync("payments", requestBody, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"O Asaas recusou a criação da cobrança com HTTP {(int)response.StatusCode}.");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasPaymentResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id))
        {
            throw new ExternalOperationNotAllowedException("O Asaas não retornou o identificador da cobrança criada.");
        }

        return new AsaasChargeCreation(responseData.Id, responseData.BankSlipUrl);
    }

    private sealed record AsaasPaymentResponse(string? Id, string? BankSlipUrl);
}
