namespace Evoque.Billing.Api.Contracts;

public sealed record EvoEmployeeResponse(
    int Id,
    int BranchId,
    string BranchName,
    string Name,
    string Status,
    string? Email,
    string? JobPosition);

public sealed record EvoEmployeeListResponse(
    IReadOnlyCollection<EvoEmployeeResponse> Employees,
    int Offset,
    int Limit);

public sealed record EvoMembershipResponse(
    int Id,
    int MemberMembershipId,
    string Name,
    string? Status,
    decimal? NextMonthValue,
    decimal? NextChargeValue,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    DateTimeOffset? NextChargeDate);

public sealed record EvoMemberResponse(
    int Id,
    int BranchId,
    string BranchName,
    string FirstName,
    string? LastName,
    IReadOnlyCollection<EvoMembershipResponse> Memberships);

public sealed record EvoMemberListResponse(
    IReadOnlyCollection<EvoMemberResponse> Members,
    int Offset,
    int Limit);

public sealed record EvoCompanyResponse(
    int PartnershipId,
    string PartnershipDescription,
    int Id,
    int BranchId,
    string CorporateName,
    string? TradeName,
    string? TaxId,
    bool IsActive);

public sealed record EvoCompanyListResponse(
    IReadOnlyCollection<EvoCompanyResponse> Companies,
    int PartnershipCount);

public sealed record EvoBranchResponse(int Id, string Name);

public sealed record EvoBranchGroupResponse(
    int Id,
    string Name,
    IReadOnlyCollection<EvoBranchResponse> Branches);
