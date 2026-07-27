using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Integrations.Evo;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class CorporateBillingPreviewServiceTests
{
    [Fact]
    public void PartnershipResolver_PrefersItemPartnershipAndKeepsMemberMembership()
    {
        var resolver = new EvoCorporatePartnershipResolver();
        var sale = new EvoSale(
            100,
            200,
            300,
            "Empresa Geral",
            Items:
            [
                new EvoSaleItem(
                    400,
                    500,
                    600,
                    "Plano corporativo",
                    89.90m,
                    89.90m,
                    0m,
                    300,
                    "Empresa do Item"),
            ]);

        var resolution = resolver.Resolve(sale);

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasConflict);
        Assert.Equal(300, resolution.PartnershipId);
        Assert.Equal("Empresa do Item", resolution.PartnershipName);
        Assert.Equal(600, resolution.MemberMembershipId);
    }

    [Fact]
    public void PartnershipResolver_BlocksDifferentPartnershipsBetweenSaleAndItem()
    {
        var resolver = new EvoCorporatePartnershipResolver();
        var sale = new EvoSale(
            100,
            200,
            300,
            "Empresa A",
            Items:
            [
                new EvoSaleItem(400, 500, 600, "Plano", 10m, 10m, 0m, 301, "Empresa B"),
            ]);

        var resolution = resolver.Resolve(sale);

        Assert.False(resolution.IsResolved);
        Assert.True(resolution.HasConflict);
    }

    [Fact]
    public async Task CreateAsync_DeduplicatesReceivablesAndGroupsAmountsInCents()
    {
        var gateway = new PreviewEvoDirectoryGateway();
        var service = new CorporateBillingPreviewService(
            gateway,
            new EvoCorporatePartnershipResolver());

        var response = await service.CreateAsync(
            new CreateCorporateBillingPreviewRequest(2026, 7, 50, 20, 0),
            CancellationToken.None);

        var company = Assert.Single(response.Companies);
        Assert.Equal(300, company.PartnershipId);
        Assert.Equal(8990, company.TotalAmountCents);
        Assert.Equal(89.90m, company.TotalAmount);
        Assert.Equal(1, response.DuplicateReceivablesIgnored);
        Assert.True(response.IsComplete);
    }

    private sealed class PreviewEvoDirectoryGateway : IEvoDirectoryGateway
    {
        public Task<IReadOnlyCollection<EvoReceivable>> ListReceivablesAsync(
            DateOnly competenceDateStart,
            DateOnly competenceDateEnd,
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<EvoReceivable> receivables = skip == 0
                ?
                [
                    CreateReceivable(),
                    CreateReceivable(),
                ]
                : [];
            return Task.FromResult(receivables);
        }

        public Task<EvoSale> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new EvoSale(
                saleId,
                200,
                300,
                "Empresa Parceira",
                Items:
                [
                    new EvoSaleItem(400, 500, 600, "Plano", 89.90m, 89.90m, 0m, 300, "Empresa Parceira"),
                ]));
        }

        public Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
            string? name,
            string? email,
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoEmployee>>([]);
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

        public Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
            int status,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoPartnership>>([]);
        }

        public Task<IReadOnlyCollection<EvoMemberMembership>> ListMemberMembershipsAsync(
            int take,
            int skip,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoMemberMembership>>([]);
        }

        public Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<EvoBranchGroup>>([]);
        }

        private static EvoReceivable CreateReceivable()
        {
            return new EvoReceivable(
                10,
                100,
                200,
                "Pessoa de Teste",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 20),
                89.90m,
                1,
                "Open",
                5,
                "Bank slip",
                1,
                1);
        }
    }
}
