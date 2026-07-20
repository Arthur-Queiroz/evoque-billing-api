using Evoque.Billing.Api.Integrations.Evo;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class EvoDirectoryServiceTests
{
    [Fact]
    public async Task ListEmployeesAsync_NormalizesPaginationAndDoesNotExposeSensitiveFields()
    {
        var gateway = new RecordingEvoDirectoryGateway();
        var service = new EvoDirectoryService(gateway);

        var response = await service.ListEmployeesAsync(
            null,
            null,
            offset: -10,
            limit: 500,
            CancellationToken.None);

        Assert.Equal(0, gateway.LastEmployeeSkip);
        Assert.Equal(50, gateway.LastEmployeeTake);
        var employee = Assert.Single(response.Employees);
        Assert.Equal("Marina", employee.Name);
        Assert.Equal("Analista", employee.JobPosition);
    }

    [Fact]
    public async Task ListCompaniesAsync_IgnoresPartnershipsWithoutCompanies()
    {
        var service = new EvoDirectoryService(new RecordingEvoDirectoryGateway());

        var response = await service.ListCompaniesAsync(CancellationToken.None);

        Assert.Equal(2, response.PartnershipCount);
        var company = Assert.Single(response.Companies);
        Assert.Equal("Empresa Parceira", company.CorporateName);
        Assert.True(company.IsActive);
    }

    private sealed class RecordingEvoDirectoryGateway : IEvoDirectoryGateway
    {
        public int LastEmployeeTake { get; private set; }

        public int LastEmployeeSkip { get; private set; }

        public Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
            string? name,
            string? email,
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            LastEmployeeTake = take;
            LastEmployeeSkip = skip;
            return Task.FromResult<IReadOnlyCollection<EvoEmployee>>(
            [
                new EvoEmployee(1, 10, "Unidade Centro", "Marina", "Ativo", "marina@example.com", "Analista"),
            ]);
        }

        public Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
            int status,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoPartnership>>(
            [
                new EvoPartnership(1, "Convênio A", false, false, new EvoCompany(
                    100,
                    10,
                    "Empresa Parceira",
                    "Parceira",
                    "12345678000199",
                    false)),
                new EvoPartnership(2, "Sem empresa", false, false, null),
            ]);
        }

        public Task<IReadOnlyCollection<EvoMember>> ListMembersAsync(
            int status,
            int take,
            int skip,
            bool includeMemberships,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoMember>>([]);
        }

        public Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoBranchGroup>>([]);
        }
    }
}
