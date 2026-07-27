using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public interface IAsaasCustomerGateway
{
    Task<AsaasCustomerPage> ListAsync(
        string? searchTerm,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<AsaasCustomerLookupResult> FindByTaxIdAsync(
        AsaasEnvironment asaasEnvironment,
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

public enum AsaasCustomerLookupStatus
{
    NotFound,
    Found,
    Ambiguous,
}

public sealed record AsaasCustomerLookupResult(
    AsaasCustomerLookupStatus Status,
    AsaasCustomer? Customer,
    int MatchCount)
{
    public static AsaasCustomerLookupResult NotFound()
        => new(AsaasCustomerLookupStatus.NotFound, null, 0);

    public static AsaasCustomerLookupResult Found(AsaasCustomer customer)
        => new(AsaasCustomerLookupStatus.Found, customer, 1);

    public static AsaasCustomerLookupResult Ambiguous(int matchCount)
        => new(AsaasCustomerLookupStatus.Ambiguous, null, matchCount);
}
