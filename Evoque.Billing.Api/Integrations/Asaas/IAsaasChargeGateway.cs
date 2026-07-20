using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasChargeGateway
{
    Task<AsaasChargeCreation> CreateChargeAsync(
        AsaasEnvironment asaasEnvironment,
        AsaasChargeRequest request,
        CancellationToken cancellationToken);
}

public sealed record AsaasChargeRequest(
    string CustomerId,
    decimal Amount,
    DateOnly DueDate,
    string Description,
    string ExternalReference);

public sealed record AsaasChargeCreation(string PaymentId, string? BankSlipUrl);
