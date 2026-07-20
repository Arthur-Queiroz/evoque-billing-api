namespace Evoque.Billing.Api.Integrations.Evo;

public interface IEvoDirectoryGateway
{
    Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
        string? name,
        string? email,
        int take,
        int skip,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<EvoMember>> ListMembersAsync(
        int status,
        int take,
        int skip,
        bool includeMemberships,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
        int status,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(CancellationToken cancellationToken);
}

public sealed record EvoEmployee(
    int Id,
    int BranchId,
    string BranchName,
    string Name,
    string Status,
    string? Email,
    string? JobPosition);

public sealed record EvoMember(
    int Id,
    int BranchId,
    string BranchName,
    string FirstName,
    string? LastName,
    IReadOnlyCollection<EvoMembership> Memberships);

public sealed record EvoMembership(
    int Id,
    int MemberMembershipId,
    string Name,
    string? Status,
    decimal? NextMonthValue,
    decimal? NextChargeValue,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    DateTimeOffset? NextChargeDate);

public sealed record EvoPartnership(
    int Id,
    string Description,
    bool IsBlocked,
    bool IsInactive,
    EvoCompany? Company);

public sealed record EvoCompany(
    int Id,
    int BranchId,
    string CorporateName,
    string? TradeName,
    string? TaxId,
    bool IsDeleted);

public sealed record EvoBranchGroup(int Id, string Name, IReadOnlyCollection<EvoBranch> Branches);

public sealed record EvoBranch(int Id, string Name);
