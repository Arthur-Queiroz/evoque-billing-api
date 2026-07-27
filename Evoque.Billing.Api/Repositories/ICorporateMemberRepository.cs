using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface ICorporateMemberRepository
{
    Task<IReadOnlyCollection<CorporateMember>> ListAsync(CancellationToken cancellationToken);

    Task UpsertManyAsync(
        IReadOnlyCollection<CorporateMember> corporateMembers,
        CancellationToken cancellationToken);
}
