using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryCorporateMemberRepository(InMemoryBillingDataStore dataStore)
    : ICorporateMemberRepository
{
    public Task<IReadOnlyCollection<CorporateMember>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<CorporateMember>>(
            dataStore.CorporateMembers.Values
                .OrderBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.EvoMemberId)
                .ToArray());
    }

    public Task UpsertManyAsync(
        IReadOnlyCollection<CorporateMember> corporateMembers,
        CancellationToken cancellationToken)
    {
        foreach (var corporateMember in corporateMembers)
        {
            dataStore.CorporateMembers[corporateMember.EvoMemberId] = corporateMember;
        }

        return Task.CompletedTask;
    }
}
