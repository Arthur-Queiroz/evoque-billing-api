using System.Net.Http.Headers;
using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.Evo;

public sealed class EvoDirectoryGateway(
    HttpClient httpClient,
    IOptions<EvoOptions> evoOptions) : IEvoDirectoryGateway
{
    public async Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
        string? name,
        string? email,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        var queryParts = new List<string>
        {
            $"take={take}",
            $"skip={skip}",
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            queryParts.Add($"name={Uri.EscapeDataString(name.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            queryParts.Add($"email={Uri.EscapeDataString(email.Trim())}");
        }

        using var response = await httpClient.GetAsync(
            $"api/v2/employees?{string.Join("&", queryParts)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar colaboradores no Evo (HTTP {(int)response.StatusCode}).");
        }

        var employees = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoEmployeeResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar colaboradores.");

        return employees.Select(employee => new EvoEmployee(
            employee.IdEmployee,
            employee.IdBranch,
            employee.BranchName,
            employee.Name,
            employee.Status,
            employee.CurrentEmail,
            employee.JobPosition)).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
        int status,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync(
            $"api/v1/partnership?status={status}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar empresas e convênios no Evo (HTTP {(int)response.StatusCode}).");
        }

        var partnerships = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoPartnershipResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar empresas e convênios.");

        return partnerships.Select(partnership => new EvoPartnership(
            partnership.IdPartnership,
            partnership.Description,
            partnership.IsBlockedFlag,
            partnership.IsInactiveFlag,
            partnership.Company is null
                ? null
                : new EvoCompany(
                    partnership.Company.AgreementCompanyId,
                    partnership.Company.BranchId,
                    partnership.Company.CorporateName,
                    partnership.Company.TradeName,
                    partnership.Company.Cnpj,
                    partnership.Company.IsDeletedFlag))).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoMember>> ListMembersAsync(
        int status,
        int take,
        int skip,
        bool includeMemberships,
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync(
            $"api/v2/members?status={status}&take={take}&skip={skip}&showMemberships={includeMemberships.ToString().ToLowerInvariant()}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar membros no Evo (HTTP {(int)response.StatusCode}).");
        }

        var members = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoMemberResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar membros.");

        return members.Select(member => new EvoMember(
            member.IdMember,
            member.IdBranch,
            member.BranchName,
            member.FirstName,
            member.LastName,
            member.Memberships.Select(membership => new EvoMembership(
                membership.IdMembership,
                membership.IdMemberMembership,
                membership.Name,
                membership.MembershipStatus,
                membership.ValueNextMonth,
                membership.NextCharge,
                membership.StartDate,
                membership.EndDate,
                membership.NextDateSuspension)).ToArray())).ToArray();
    }

    public async Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(
        CancellationToken cancellationToken)
    {
        ConfigureHttpClient(httpClient, evoOptions.Value);

        using var response = await httpClient.GetAsync("api/v1/configuration/group-branches", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar grupos de filiais no Evo (HTTP {(int)response.StatusCode}).");
        }

        var branchGroups = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<EvoBranchGroupResponse>>(
            cancellationToken: cancellationToken)
            ?? throw new ExternalOperationNotAllowedException(
                "O Evo retornou uma resposta inválida ao consultar grupos de filiais.");

        return branchGroups.Select(branchGroup => new EvoBranchGroup(
            branchGroup.GroupId,
            branchGroup.GroupName,
            branchGroup.Branches.Select(branch => new EvoBranch(branch.BranchId, branch.BranchName)).ToArray())).ToArray();
    }

    private static void ConfigureHttpClient(HttpClient client, EvoOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ExternalOperationNotAllowedException("A URL da API Evo deve ser HTTPS e absoluta.");
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ExternalOperationNotAllowedException(
                "O usuário e o token da API Evo não estão configurados.");
        }

        var credentialValue = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.ApiKey}"));
        client.BaseAddress = baseUri;
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentialValue);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvoqueBilling/0.1");
    }

    private sealed record EvoEmployeeResponse(
        int IdEmployee,
        int IdBranch,
        string BranchName,
        string Name,
        string Status,
        string? CurrentEmail,
        string? JobPosition);

    private sealed record EvoMemberResponse(
        int IdMember,
        int IdBranch,
        string BranchName,
        string FirstName,
        string? LastName,
        IReadOnlyCollection<EvoMembershipResponse> Memberships);

    private sealed record EvoMembershipResponse(
        int IdMembership,
        int IdMemberMembership,
        string Name,
        string? MembershipStatus,
        decimal? ValueNextMonth,
        decimal? NextCharge,
        DateTimeOffset? StartDate,
        DateTimeOffset? EndDate,
        DateTimeOffset? NextDateSuspension);

    private sealed record EvoPartnershipResponse(
        int IdPartnership,
        string Description,
        bool IsBlockedFlag,
        bool IsInactiveFlag,
        EvoCompanyResponse? Company);

    private sealed record EvoCompanyResponse(
        int AgreementCompanyId,
        int BranchId,
        string CorporateName,
        string? TradeName,
        string? Cnpj,
        bool IsDeletedFlag);

    private sealed record EvoBranchGroupResponse(
        int GroupId,
        string GroupName,
        IReadOnlyCollection<EvoBranchResponse> Branches);

    private sealed record EvoBranchResponse(int BranchId, string BranchName);
}
