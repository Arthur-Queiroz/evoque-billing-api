namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasCustomerGateway
{
    Task<AsaasCustomerPage> ListAsync(
        string? searchTerm,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<AsaasCustomer?> FindSandboxByTaxIdAsync(
        string taxId,
        CancellationToken cancellationToken);

    Task<AsaasCustomer> CreateSandboxAsync(
        string name,
        string taxId,
        string email,
        CancellationToken cancellationToken);
}

public sealed record AsaasCustomerPage(
    IReadOnlyCollection<AsaasCustomer> Customers,
    bool HasMore,
    int TotalCount);

public sealed record AsaasCustomer(
    string Id,
    string Name,
    string? TaxId,
    string? Email,
    string? AdditionalEmails);
