using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface ICompanyCatalogImportRepository
{
    Task AddAsync(
        CompanyCatalogImport companyCatalogImport,
        IReadOnlyCollection<CompanyCatalogImportMember> importedMembers,
        CancellationToken cancellationToken);

    Task<CompanyCatalogImport?> FindLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pessoas encontradas para uma empresa na sincronização mais recente em que
    /// ela apareceu. Devolve uma coleção vazia quando a empresa nunca apareceu.
    /// </summary>
    Task<IReadOnlyCollection<CompanyCatalogImportMember>> ListLatestMembersByCompanyAsync(
        string companyTaxId,
        CancellationToken cancellationToken);
}
