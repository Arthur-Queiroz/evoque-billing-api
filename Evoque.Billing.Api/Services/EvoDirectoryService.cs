using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Integrations.Evo;

namespace Evoque.Billing.Api.Services;

public sealed class EvoDirectoryService(IEvoDirectoryGateway evoDirectoryGateway)
{
    public async Task<EvoEmployeeListResponse> ListEmployeesAsync(
        string? name,
        string? email,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeOffset = Math.Max(offset, 0);
        var safeLimit = Math.Clamp(limit, 1, 50);
        var employees = await evoDirectoryGateway.ListEmployeesAsync(
            name,
            email,
            safeLimit,
            safeOffset,
            cancellationToken);

        return new EvoEmployeeListResponse(
            employees.Select(employee => new EvoEmployeeResponse(
                employee.Id,
                employee.BranchId,
                employee.BranchName,
                employee.Name,
                employee.Status,
                employee.Email,
                employee.JobPosition)).ToArray(),
            safeOffset,
            safeLimit);
    }

    public async Task<EvoCompanyListResponse> ListCompaniesAsync(CancellationToken cancellationToken)
    {
        var partnerships = await evoDirectoryGateway.ListPartnershipsAsync(0, cancellationToken);
        var companies = partnerships
            .Where(partnership => partnership.Company is not null)
            .Select(partnership => new EvoCompanyResponse(
                partnership.Id,
                partnership.Description,
                partnership.Company!.Id,
                partnership.Company.BranchId,
                partnership.Company.CorporateName,
                partnership.Company.TradeName,
                partnership.Company.TaxId,
                !partnership.IsBlocked && !partnership.IsInactive && !partnership.Company.IsDeleted))
            .OrderBy(company => company.CorporateName)
            .ToArray();

        return new EvoCompanyListResponse(companies, partnerships.Count);
    }

    public async Task<EvoMemberListResponse> ListMembersAsync(
        int status,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeStatus = status is 1 or 2 ? status : 1;
        var safeOffset = Math.Max(offset, 0);
        var safeLimit = Math.Clamp(limit, 1, 50);
        var members = await evoDirectoryGateway.ListMembersAsync(
            safeStatus,
            safeLimit,
            safeOffset,
            includeMemberships: true,
            cancellationToken);

        return new EvoMemberListResponse(
            members.Select(member => new EvoMemberResponse(
                member.Id,
                member.BranchId,
                member.BranchName,
                member.FirstName,
                member.LastName,
                member.Memberships.Select(membership => new EvoMembershipResponse(
                    membership.Id,
                    membership.MemberMembershipId,
                    membership.Name,
                    membership.Status,
                    membership.NextMonthValue,
                    membership.NextChargeValue,
                    membership.StartDate,
                    membership.EndDate,
                    membership.NextChargeDate)).ToArray())).ToArray(),
            safeOffset,
            safeLimit);
    }

    public async Task<IReadOnlyCollection<EvoBranchGroupResponse>> ListBranchGroupsAsync(
        CancellationToken cancellationToken)
    {
        var branchGroups = await evoDirectoryGateway.ListBranchGroupsAsync(cancellationToken);
        return branchGroups.Select(branchGroup => new EvoBranchGroupResponse(
            branchGroup.Id,
            branchGroup.Name,
            branchGroup.Branches.Select(branch => new EvoBranchResponse(branch.Id, branch.Name)).ToArray())).ToArray();
    }
}
