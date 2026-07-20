using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;

namespace Evoque.Billing.Api.Services;

public sealed class AsaasCustomerService(
    IAsaasCustomerGateway asaasCustomerGateway,
    IAsaasCustomerNotificationGateway asaasCustomerNotificationGateway)
{
    public async Task<AsaasCustomerListResponse> ListAsync(
        string? searchTerm,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeOffset = Math.Max(offset, 0);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var customerPage = await asaasCustomerGateway.ListAsync(
            searchTerm,
            safeOffset,
            safeLimit,
            cancellationToken);

        var customers = new List<AsaasCustomerResponse>();
        foreach (var asaasCustomer in customerPage.Customers)
        {
            var emailDeliveryReadiness = await asaasCustomerNotificationGateway.GetEmailDeliveryReadinessAsync(
                AsaasEnvironment.Sandbox,
                asaasCustomer.Id,
                cancellationToken);
            customers.Add(new AsaasCustomerResponse(
                asaasCustomer.Id,
                asaasCustomer.Name,
                asaasCustomer.TaxId,
                asaasCustomer.Email,
                emailDeliveryReadiness.HasEmailRecipient,
                emailDeliveryReadiness.PaymentCreatedEmailEnabled));
        }

        return new AsaasCustomerListResponse(
            customers,
            customerPage.HasMore,
            safeOffset,
            customerPage.TotalCount);
    }
}
