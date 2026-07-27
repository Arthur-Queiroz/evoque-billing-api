using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryCompanyCatalogImportRepository(InMemoryBillingDataStore dataStore)
    : ICompanyCatalogImportRepository
{
    public Task AddAsync(
        CompanyCatalogImport companyCatalogImport,
        IReadOnlyCollection<CompanyCatalogImportMember> importedMembers,
        CancellationToken cancellationToken)
    {
        dataStore.CompanyCatalogImports[companyCatalogImport.Id] = companyCatalogImport;
        dataStore.CompanyCatalogImportMembers[companyCatalogImport.Id] = importedMembers;
        return Task.CompletedTask;
    }

    public Task<CompanyCatalogImport?> FindLatestAsync(CancellationToken cancellationToken)
    {
        var latestImport = dataStore.CompanyCatalogImports.Values
            .OrderByDescending(companyCatalogImport => companyCatalogImport.SynchronizedAt)
            .FirstOrDefault();
        return Task.FromResult(latestImport);
    }

    public Task<IReadOnlyCollection<CompanyCatalogImportMember>> ListLatestMembersByCompanyAsync(
        string companyTaxId,
        CancellationToken cancellationToken)
    {
        var importsFromNewestToOldest = dataStore.CompanyCatalogImports.Values
            .OrderByDescending(companyCatalogImport => companyCatalogImport.SynchronizedAt);
        foreach (var companyCatalogImport in importsFromNewestToOldest)
        {
            if (!dataStore.CompanyCatalogImportMembers.TryGetValue(companyCatalogImport.Id, out var members))
            {
                continue;
            }

            var companyMembers = members
                .Where(member => member.CompanyTaxId == companyTaxId)
                .OrderBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (companyMembers.Length > 0)
            {
                return Task.FromResult<IReadOnlyCollection<CompanyCatalogImportMember>>(companyMembers);
            }
        }

        return Task.FromResult<IReadOnlyCollection<CompanyCatalogImportMember>>([]);
    }
}
