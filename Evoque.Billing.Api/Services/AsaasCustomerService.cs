using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using System.Net.Mail;

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

    public async Task<CreateSandboxAsaasCustomerResponse> CreateSandboxAsync(
        CreateSandboxAsaasCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var taxId = new string((request.TaxId ?? string.Empty).Where(char.IsDigit).ToArray());
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("O nome da empresa é obrigatório.");
        }

        if (taxId.Length != 14)
        {
            throw new ValidationException("O CNPJ da empresa deve possuir 14 dígitos.");
        }

        if (!MailAddress.TryCreate(email, out _))
        {
            throw new ValidationException("O e-mail do cliente Sandbox é inválido.");
        }

        var existingCustomer = await asaasCustomerGateway.FindSandboxByTaxIdAsync(
            taxId,
            cancellationToken);
        if (existingCustomer is not null)
        {
            return new CreateSandboxAsaasCustomerResponse(
                await CreateResponseAsync(existingCustomer, cancellationToken),
                false);
        }

        var createdCustomer = await asaasCustomerGateway.CreateSandboxAsync(
            name,
            taxId,
            email!,
            cancellationToken);
        return new CreateSandboxAsaasCustomerResponse(
            await CreateResponseAsync(createdCustomer, cancellationToken),
            true);
    }

    private async Task<AsaasCustomerResponse> CreateResponseAsync(
        AsaasCustomer asaasCustomer,
        CancellationToken cancellationToken)
    {
        var emailDeliveryReadiness = await asaasCustomerNotificationGateway.GetEmailDeliveryReadinessAsync(
            AsaasEnvironment.Sandbox,
            asaasCustomer.Id,
            cancellationToken);
        return new AsaasCustomerResponse(
            asaasCustomer.Id,
            asaasCustomer.Name,
            asaasCustomer.TaxId,
            asaasCustomer.Email,
            emailDeliveryReadiness.HasEmailRecipient,
            emailDeliveryReadiness.PaymentCreatedEmailEnabled);
    }
}
