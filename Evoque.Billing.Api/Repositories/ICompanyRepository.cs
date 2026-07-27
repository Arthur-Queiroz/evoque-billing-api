using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface ICompanyRepository
{
    Task<Company?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Company>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(Company company, CancellationToken cancellationToken);
}
