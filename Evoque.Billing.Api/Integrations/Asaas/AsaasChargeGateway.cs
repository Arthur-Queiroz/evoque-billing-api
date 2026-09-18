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
            var refusalReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                $"O Asaas recusou a criação da cobrança: {refusalReason}");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasPaymentResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id))
        {
            throw new ExternalOperationNotAllowedException("O Asaas não retornou o identificador da cobrança criada.");
        }

        return new AsaasChargeCreation(responseData.Id, responseData.BankSlipUrl);
    }

    public async Task<AsaasChargeState> GetChargeAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asaasPaymentId))
        {
            throw new ValidationException("O identificador da cobrança Asaas é obrigatório para consultá-la.");
        }

        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateReadOperation(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        using var response = await httpClient.GetAsync(
            $"payments/{Uri.EscapeDataString(asaasPaymentId)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failureReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar a cobrança no Asaas: {failureReason}");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasPaymentStateResponse>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id) || string.IsNullOrWhiteSpace(responseData.Status))
        {
            throw new ExternalOperationNotAllowedException(
                "O Asaas retornou uma resposta inválida ao consultar a cobrança.");
        }

        return new AsaasChargeState(
            responseData.Id,
            responseData.Status,
            DateOnly.TryParse(responseData.PaymentDate, out var paymentDate) ? paymentDate : null);
    }

    private sealed record AsaasPaymentResponse(string? Id, string? BankSlipUrl);

    private sealed record AsaasPaymentStateResponse(string? Id, string? Status, string? PaymentDate);
}
