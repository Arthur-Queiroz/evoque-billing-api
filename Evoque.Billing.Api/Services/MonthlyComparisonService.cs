using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

public sealed class MonthlyComparisonService
{
    public IReadOnlyCollection<ComparisonResult> Compare(
        IReadOnlyCollection<CompanyBillingSnapshot> previousCompanies,
        IReadOnlyCollection<CompanyBillingSnapshot> currentCompanies)
    {
        ValidateCompanies(previousCompanies);
        ValidateCompanies(currentCompanies);

        var previousCompaniesById = previousCompanies.ToDictionary(company => company.ExternalCompanyId);
        var currentCompaniesById = currentCompanies.ToDictionary(company => company.ExternalCompanyId);
        var companyIds = previousCompaniesById.Keys
            .Union(currentCompaniesById.Keys)
            .OrderBy(companyId => companyId);

        var comparisonResults = new List<ComparisonResult>();
        foreach (var companyId in companyIds)
        {
            previousCompaniesById.TryGetValue(companyId, out var previousCompany);
            currentCompaniesById.TryGetValue(companyId, out var currentCompany);

            var changes = CompareMembers(previousCompany?.Members ?? [], currentCompany?.Members ?? []);
            if (changes.Count == 0 && previousCompany is not null && currentCompany is not null)
            {
                continue;
            }

            comparisonResults.Add(new ComparisonResult(
                companyId,
                currentCompany?.CompanyName ?? previousCompany!.CompanyName,
                previousCompany?.TotalAmount ?? 0,
                currentCompany?.TotalAmount ?? 0,
                changes));
        }

        return comparisonResults;
    }

    private static IReadOnlyCollection<MemberComparison> CompareMembers(
        IReadOnlyCollection<MemberBillingSnapshot> previousMembers,
        IReadOnlyCollection<MemberBillingSnapshot> currentMembers)
    {
        var previousMembersById = previousMembers.ToDictionary(member => member.ExternalMemberId);
        var currentMembersById = currentMembers.ToDictionary(member => member.ExternalMemberId);
        var memberIds = previousMembersById.Keys
            .Union(currentMembersById.Keys)
            .OrderBy(memberId => memberId);

        var changes = new List<MemberComparison>();
        foreach (var memberId in memberIds)
        {
            previousMembersById.TryGetValue(memberId, out var previousMember);
            currentMembersById.TryGetValue(memberId, out var currentMember);
            var change = GetChange(previousMember, currentMember);
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    private static MemberComparison? GetChange(
        MemberBillingSnapshot? previousMember,
        MemberBillingSnapshot? currentMember)
    {
        if (previousMember is null)
        {
            return new MemberComparison(
                currentMember!.ExternalMemberId,
                currentMember.MemberName,
                MemberComparisonType.Added,
                0,
                currentMember.IsActive ? currentMember.MonthlyAmount : 0);
        }

        if (currentMember is null)
        {
            return new MemberComparison(
                previousMember.ExternalMemberId,
                previousMember.MemberName,
                MemberComparisonType.Removed,
                previousMember.IsActive ? previousMember.MonthlyAmount : 0,
                0);
        }

        if (!previousMember.IsActive && currentMember.IsActive)
        {
            return new MemberComparison(
                currentMember.ExternalMemberId,
                currentMember.MemberName,
                MemberComparisonType.Activated,
                0,
                currentMember.MonthlyAmount);
        }

        if (previousMember.IsActive && !currentMember.IsActive)
        {
            return new MemberComparison(
                currentMember.ExternalMemberId,
                currentMember.MemberName,
                MemberComparisonType.Deactivated,
                previousMember.MonthlyAmount,
                0);
        }

        if (previousMember.IsActive && currentMember.IsActive && previousMember.MonthlyAmount != currentMember.MonthlyAmount)
        {
            return new MemberComparison(
                currentMember.ExternalMemberId,
                currentMember.MemberName,
                MemberComparisonType.AmountChanged,
                previousMember.MonthlyAmount,
                currentMember.MonthlyAmount);
        }

        return null;
    }

    private static void ValidateCompanies(IReadOnlyCollection<CompanyBillingSnapshot> companies)
    {
        var duplicatedCompanyIds = companies
            .GroupBy(company => company.ExternalCompanyId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicatedCompanyIds.Length > 0)
        {
            throw new ValidationException("O snapshot contém empresas duplicadas.");
        }

        foreach (var company in companies)
        {
            if (string.IsNullOrWhiteSpace(company.ExternalCompanyId) || string.IsNullOrWhiteSpace(company.CompanyName))
            {
                throw new ValidationException("Cada empresa do snapshot precisa de identificador e nome.");
            }

            var duplicatedMemberIds = company.Members
                .GroupBy(member => member.ExternalMemberId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicatedMemberIds.Length > 0)
            {
                throw new ValidationException($"A empresa {company.CompanyName} contém colaboradores duplicados.");
            }

            foreach (var member in company.Members)
            {
                member.Validate();
            }
        }
    }
}
