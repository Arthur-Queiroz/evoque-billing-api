using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class CorporateMemberService(
    ICorporateMemberRepository corporateMemberRepository,
    ICompanyRepository companyRepository)
{
    public async Task<IReadOnlyCollection<CorporateMemberResponse>> ListAsync(
        ListCorporateMembersQuery query,
        CancellationToken cancellationToken)
    {
        var corporateMembers = await corporateMemberRepository.ListAsync(cancellationToken);
        var companies = await companyRepository.ListAsync(cancellationToken);
        var companyNamesByTaxId = companies.ToDictionary(
            company => company.TaxId,
            company => company.DisplayName,
            StringComparer.Ordinal);

        return corporateMembers
            .Select(member => CorporateMemberResponse.FromDomain(
                member,
                companyNamesByTaxId.GetValueOrDefault(
                    member.CompanyTaxId,
                    CompanyTaxId.Format(member.CompanyTaxId))))
            .Where(member => MatchesQuery(member, query))
            .OrderBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.EvoMemberId)
            .ToArray();
    }

    public async Task<CorporateMemberComparisonResponse> CompareAsync(
        ImportedCompanyCatalog importedCatalog,
        CancellationToken cancellationToken)
    {
        var existingMembers = await corporateMemberRepository.ListAsync(cancellationToken);
        return Compare(importedCatalog, existingMembers).Response;
    }

    public async Task<CorporateMemberComparisonResponse> ApplyCompleteSnapshotAsync(
        ImportedCompanyCatalog importedCatalog,
        Guid importId,
        string operatorId,
        DateTimeOffset synchronizedAt,
        bool completeSnapshotConfirmed,
        CancellationToken cancellationToken)
    {
        if (!completeSnapshotConfirmed)
        {
            throw new ValidationException(
                "Confirme que o arquivo contém a exportação completa de clientes ativos do CRM 2.0.");
        }

        var existingMembers = await corporateMemberRepository.ListAsync(cancellationToken);
        var comparison = Compare(importedCatalog, existingMembers);
        var existingMembersById = existingMembers.ToDictionary(member => member.EvoMemberId);
        var membersToPersist = new List<CorporateMember>();

        foreach (var observation in comparison.ValidObservations)
        {
            var contracts = observation.Member.Contracts.Select(contract =>
                new CorporateMemberContract(
                    contract.ContractKey,
                    contract.EvoContractId,
                    contract.ContractName));
            if (!existingMembersById.TryGetValue(observation.Member.EvoMemberId, out var corporateMember))
            {
                corporateMember = CorporateMember.Create(
                    observation.Member.EvoMemberId,
                    observation.Member.MemberName,
                    observation.CompanyTaxId,
                    contracts,
                    importId,
                    operatorId,
                    synchronizedAt);
            }
            else
            {
                corporateMember.RegisterObservation(
                    observation.Member.MemberName,
                    contracts,
                    importId,
                    operatorId,
                    synchronizedAt);
            }

            membersToPersist.Add(corporateMember);
        }

        foreach (var departedMemberId in comparison.DepartedMemberIds)
        {
            var departedMember = existingMembersById[departedMemberId];
            departedMember.Deactivate(importId, operatorId, synchronizedAt);
            membersToPersist.Add(departedMember);
        }

        await corporateMemberRepository.UpsertManyAsync(membersToPersist, cancellationToken);
        return comparison.Response;
    }

    private static CorporateMemberComparison Compare(
        ImportedCompanyCatalog importedCatalog,
        IReadOnlyCollection<CorporateMember> existingMembers)
    {
        var existingMembersById = existingMembers.ToDictionary(member => member.EvoMemberId);
        var allObservations = importedCatalog.Companies
            .SelectMany(company => company.Members.Select(member =>
                new CorporateMemberObservation(company.TaxId, member)))
            .ToArray();
        var observedMemberIds = allObservations
            .Select(observation => observation.Member.EvoMemberId)
            .ToHashSet();

        var validObservations = new List<CorporateMemberObservation>();
        var newMemberCount = 0;
        var retainedMemberCount = 0;
        var reactivatedMemberCount = 0;
        var conflictMemberCount = 0;

        foreach (var observationsByMember in allObservations.GroupBy(
                     observation => observation.Member.EvoMemberId))
        {
            var distinctCompanyTaxIds = observationsByMember
                .Select(observation => observation.CompanyTaxId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctCompanyTaxIds.Length != 1)
            {
                conflictMemberCount++;
                continue;
            }

            var observation = observationsByMember.First();
            if (existingMembersById.TryGetValue(
                    observation.Member.EvoMemberId,
                    out var existingMember)
                && !string.Equals(
                    existingMember.CompanyTaxId,
                    observation.CompanyTaxId,
                    StringComparison.Ordinal))
            {
                conflictMemberCount++;
                continue;
            }

            validObservations.Add(observation);
            if (existingMember is null)
            {
                newMemberCount++;
            }
            else if (existingMember.IsActive)
            {
                retainedMemberCount++;
            }
            else
            {
                reactivatedMemberCount++;
            }
        }

        var departedMemberIds = existingMembers
            .Where(member => member.IsActive && !observedMemberIds.Contains(member.EvoMemberId))
            .Select(member => member.EvoMemberId)
            .ToArray();
        return new CorporateMemberComparison(
            new CorporateMemberComparisonResponse(
                newMemberCount,
                retainedMemberCount,
                departedMemberIds.Length,
                reactivatedMemberCount,
                conflictMemberCount),
            validObservations,
            departedMemberIds);
    }

    private static bool MatchesQuery(
        CorporateMemberResponse corporateMember,
        ListCorporateMembersQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.CompanyTaxId)
            && !string.Equals(
                corporateMember.CompanyTaxId,
                CompanyTaxId.Normalize(query.CompanyTaxId),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var wantsActive = string.Equals(query.Status, "active", StringComparison.OrdinalIgnoreCase);
            if (corporateMember.IsActive != wantsActive)
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(query.Search))
        {
            return true;
        }

        var normalizedSearch = SpreadsheetText.Normalize(query.Search);
        return SpreadsheetText.Normalize(corporateMember.MemberName)
                .Contains(normalizedSearch, StringComparison.Ordinal)
            || SpreadsheetText.Normalize(corporateMember.CompanyName)
                .Contains(normalizedSearch, StringComparison.Ordinal)
            || corporateMember.EvoMemberId.ToString().Contains(query.Search.Trim(), StringComparison.Ordinal);
    }

    private sealed record CorporateMemberObservation(
        string CompanyTaxId,
        ImportedCatalogMember Member);

    private sealed record CorporateMemberComparison(
        CorporateMemberComparisonResponse Response,
        IReadOnlyCollection<CorporateMemberObservation> ValidObservations,
        IReadOnlyCollection<long> DepartedMemberIds);
}
