using Evoque.Billing.Api.Domain;
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

    [Fact]
    public async Task ListCorporateMembersAsync_ReturnsOnlyMembersWithAnExplicitCorporatePartnership()
    {
        var gateway = new RecordingEvoDirectoryGateway();
        var service = new EvoDirectoryService(gateway);

        var response = await service.ListCorporateMembersAsync(
            offset: -1,
            limit: 100,
            CancellationToken.None);

        Assert.Equal(0, gateway.LastMemberMembershipSkip);
        Assert.Equal(5, gateway.LastMemberMembershipTake);
        Assert.Equal(2, response.ProcessedMemberMembershipCount);
        var corporateMember = Assert.Single(response.CorporateMembers);
        Assert.Equal("Colaborador Corporativo", corporateMember.MemberName);
        Assert.Equal("Empresa Parceira", corporateMember.CorporatePartnershipName);
    }

    [Fact]
    public async Task ListCorporateMembersAsync_IgnoresUnavailableSales()
    {
        var gateway = new RecordingEvoDirectoryGateway { ThrowForSaleId = 1000 };
        var service = new EvoDirectoryService(gateway);

        var response = await service.ListCorporateMembersAsync(0, 5, CancellationToken.None);

        Assert.Empty(response.CorporateMembers);
    }

    private sealed class RecordingEvoDirectoryGateway : IEvoDirectoryGateway
    {
        public int LastEmployeeTake { get; private set; }

        public int LastEmployeeSkip { get; private set; }

        public int LastMemberMembershipTake { get; private set; }

        public int LastMemberMembershipSkip { get; private set; }

        public int? ThrowForSaleId { get; init; }

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

        public Task<IReadOnlyCollection<EvoMemberMembership>> ListMemberMembershipsAsync(
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            LastMemberMembershipTake = take;
            LastMemberMembershipSkip = skip;
            return Task.FromResult<IReadOnlyCollection<EvoMemberMembership>>(
            [
                new EvoMemberMembership(1, "Colaborador Corporativo", 10, 100, 10, 1000, 89.90m, "Plano Corporativo", 1),
                new EvoMemberMembership(2, "Colaborador Individual", 20, 200, 10, 2000, 89.90m, "Plano Individual", 1),
            ]);
        }

        public Task<IReadOnlyCollection<EvoReceivable>> ListReceivablesAsync(
            DateOnly competenceDateStart,
            DateOnly competenceDateEnd,
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoReceivable>>([]);
        }

        public Task<EvoSale> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken)
        {
            if (saleId == ThrowForSaleId)
            {
                throw new EvoSaleLookupException(saleId, 403);
            }

            return Task.FromResult(saleId == 1000
                ? new EvoSale(1000, 1, 3000, "Empresa Parceira")
                : new EvoSale(2000, 2, null, null));
        }
    }
}
