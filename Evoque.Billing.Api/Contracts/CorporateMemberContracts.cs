using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

public sealed record ListCorporateMembersQuery(
    string? Search = null,
    string? Status = null,
    string? CompanyTaxId = null);

public sealed record CorporateMemberResponse(
    long EvoMemberId,
    string MemberName,
    string CompanyTaxId,
    string FormattedCompanyTaxId,
    string CompanyName,
    IReadOnlyCollection<string> Contracts,
    bool IsActive,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? DeactivatedAt)
{
    public static CorporateMemberResponse FromDomain(
        CorporateMember corporateMember,
        string companyName)
    {
        return new CorporateMemberResponse(
            corporateMember.EvoMemberId,
            corporateMember.MemberName,
            corporateMember.CompanyTaxId,
            global::Evoque.Billing.Api.Domain.CompanyTaxId.Format(corporateMember.CompanyTaxId),
            companyName,
            corporateMember.Contracts
                .Select(contract => contract.ContractName)
                .Where(contractName => !string.IsNullOrWhiteSpace(contractName))
                .Cast<string>()
                .ToArray(),
            corporateMember.IsActive,
            corporateMember.FirstSeenAt,
            corporateMember.LastSeenAt,
            corporateMember.DeactivatedAt);
    }
}

/// <summary>
/// Resultado da comparação entre a exportação do EVO e a base de colaboradores.
/// <paramref name="UnregisteredCompanyMemberCount"/> conta as pessoas cuja
/// empresa não está no catálogo: elas não são importadas nem inativadas, ficam
/// como pendência para alguém cadastrar a empresa ou corrigir o EVO.
/// </summary>
public sealed record CorporateMemberComparisonResponse(
    int NewMemberCount,
    int RetainedMemberCount,
    int DepartedMemberCount,
    int ReactivatedMemberCount,
    int ConflictMemberCount,
    int UnregisteredCompanyMemberCount);
