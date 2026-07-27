using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryCompanyRepository(InMemoryBillingDataStore dataStore) : ICompanyRepository
{
    public Task<Company?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken)
    {
        dataStore.Companies.TryGetValue(taxId, out var company);
        return Task.FromResult(company);
    }

    public Task<IReadOnlyCollection<Company>> ListAsync(CancellationToken cancellationToken)
    {
        var companies = dataStore.Companies.Values
            .OrderBy(company => company.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(company => company.TaxId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<Company>>(companies);
    }

    public Task UpsertAsync(Company company, CancellationToken cancellationToken)
    {
        dataStore.Companies[company.TaxId] = company;
        return Task.CompletedTask;
    }
}
