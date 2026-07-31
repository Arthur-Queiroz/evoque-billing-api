using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Asaas;

public sealed class AsaasCustomerNotificationGateway(
    HttpClient httpClient,
    IHostEnvironment hostEnvironment,
    IOptions<AsaasOptions> asaasOptions) : IAsaasCustomerNotificationGateway
{
    public async Task<AsaasCustomerEmailDeliveryReadiness> GetEmailDeliveryReadinessAsync(
        AsaasEnvironment asaasEnvironment,
        string customerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ValidationException("O identificador do cliente Asaas é obrigatório.");
        }

        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateReadOperation(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        var customer = await GetAsync<AsaasCustomerResponse>(
            $"customers/{Uri.EscapeDataString(customerId)}",
            cancellationToken);
        var notifications = await GetAsync<AsaasNotificationListResponse>(
            $"customers/{Uri.EscapeDataString(customerId)}/notifications",
            cancellationToken);

        var hasEmailRecipient = !string.IsNullOrWhiteSpace(customer.Email)
            || !string.IsNullOrWhiteSpace(customer.AdditionalEmails);
        var paymentCreatedEmailEnabled = notifications.Data.Any(notification =>
            notification.Event == "PAYMENT_CREATED"
            && notification.Enabled
            && notification.EmailEnabledForCustomer);

        return new AsaasCustomerEmailDeliveryReadiness(hasEmailRecipient, paymentCreatedEmailEnabled);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failureReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                "Não foi possível consultar o cadastro ou as notificações do cliente no Asaas: "
                + failureReason);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Asaas retornou uma resposta inválida ao consultar o cliente.");
    }

    private sealed record AsaasCustomerResponse(string? Email, string? AdditionalEmails);

    private sealed record AsaasNotificationListResponse(IReadOnlyCollection<AsaasNotificationResponse> Data);

    private sealed record AsaasNotificationResponse(
        string Event,
        bool Enabled,
        bool EmailEnabledForCustomer);
}
