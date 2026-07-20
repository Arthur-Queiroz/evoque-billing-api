using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasCustomerNotificationGateway
{
    Task<AsaasCustomerEmailDeliveryReadiness> GetEmailDeliveryReadinessAsync(
        AsaasEnvironment asaasEnvironment,
        string customerId,
        CancellationToken cancellationToken);
}

public sealed record AsaasCustomerEmailDeliveryReadiness(
    bool HasEmailRecipient,
    bool PaymentCreatedEmailEnabled);
