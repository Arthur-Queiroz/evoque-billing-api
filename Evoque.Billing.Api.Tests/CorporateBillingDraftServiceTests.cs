using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class CorporateBillingDraftServiceTests
{
    private const string OperatorId = "maria";
    private const string OpenSportsTaxId = "56087276000103";
    private const string WebPradoTaxId = "43322169000170";
    private static readonly BillingPeriodReference September2026 = new(2026, 9);

    [Fact]
    public async Task GenerateAsync_ReproducesTheClosingsDoneByHand()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddCompany(WebPradoTaxId, "Web Prado", 109.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        scenario.AddMembers(WebPradoTaxId, 4);

        var result = await scenario.GenerateAsync();

        var openSports = result.Created.Single(draft => draft.CompanyTaxId == OpenSportsTaxId);
        var webPrado = result.Created.Single(draft => draft.CompanyTaxId == WebPradoTaxId);
        Assert.Equal(2157.60m, openSports.TotalAmount);
        Assert.Equal(439.60m, webPrado.TotalAmount);
    }

    [Fact]
    public async Task GenerateAsync_CreatesOneItemPerMemberAndMarksThePeriodForReview()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 3);

        var result = await scenario.GenerateAsync();

        Assert.Equal(3, result.Created.Single().MemberCount);
        Assert.Equal(3, scenario.DataStore.BillingDrafts.Single().Value.Items.Count);
        Assert.Equal(
            BillingPeriodStatus.AwaitingReview,
            scenario.DataStore.BillingPeriods[September2026.ToString()].Status);
    }

    [Fact]
    public async Task GenerateAsync_SkipsACompanyWithoutAPriceAndSaysWhich()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", amountPerMember: null);
        scenario.AddMembers(OpenSportsTaxId, 24);

        var result = await scenario.GenerateAsync();

        Assert.Empty(result.Created);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(OpenSportsTaxId, skipped.CompanyTaxId);
        Assert.Contains("valor", skipped.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_SkipsAnInactiveCompany()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m, isActive: false);
        scenario.AddMembers(OpenSportsTaxId, 24);

        var result = await scenario.GenerateAsync();

        Assert.Empty(result.Created);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public async Task GenerateAsync_IgnoresAMemberWhoPaysForThemselves()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(OpenSportsTaxId, "EVOPASS RECORRENTE 79,90");

        var result = await scenario.GenerateAsync();

        Assert.Equal(2, result.Created.Single().MemberCount);
    }

    [Fact]
    public async Task GenerateAsync_IgnoresAnInactiveMember()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(
            OpenSportsTaxId,
            "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO",
            isActive: false);

        var result = await scenario.GenerateAsync();

        Assert.Equal(2, result.Created.Single().MemberCount);
    }

    [Fact]
    public async Task GenerateAsync_ReportsAnUnknownCorporateContract()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(OpenSportsTaxId, "EVOQUE CORPORATIVO RECORRENTE - 39,95");

        var result = await scenario.GenerateAsync();

        Assert.Equal(2, result.Created.Single().MemberCount);
        Assert.Contains(
            result.UnknownContracts,
            contract => contract.Contains("39,95", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_ReportsABillableMemberWithoutARegisteredCompany()
    {
        var scenario = new TestScenario();
        scenario.AddMember(WebPradoTaxId, "EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA");

        var result = await scenario.GenerateAsync();

        Assert.Empty(result.Created);
        var member = Assert.Single(result.MembersWithoutCompany);
        Assert.Equal("Colaborador 1", member.MemberName);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotDuplicateWhenRunTwice()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        await scenario.GenerateAsync();

        var secondResult = await scenario.GenerateAsync();

        Assert.Empty(secondResult.Created);
        Assert.Single(secondResult.Skipped);
        Assert.Single(scenario.DataStore.BillingDrafts);
    }

    [Fact]
    public async Task GenerateAsync_KeepsGoingAfterACompanyIsSkipped()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", amountPerMember: null);
        scenario.AddCompany(WebPradoTaxId, "Web Prado", 109.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        scenario.AddMembers(WebPradoTaxId, 4);

        var result = await scenario.GenerateAsync();

        Assert.Equal(WebPradoTaxId, result.Created.Single().CompanyTaxId);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public async Task GenerateAsync_RefusesAnUnknownBillingPeriodBeforeWritingAnything()
    {
        var scenario = new TestScenario(includeBillingPeriod: false);
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMember(OpenSportsTaxId, "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO");

        await Assert.ThrowsAsync<NotFoundException>(() => scenario.GenerateAsync());

        Assert.Empty(scenario.DataStore.BillingDrafts);
    }

    private sealed class TestScenario
    {
        private long nextMemberId = 1;

        public TestScenario(bool includeBillingPeriod = true)
        {
            DataStore = new InMemoryBillingDataStore();
            if (includeBillingPeriod)
            {
                var billingPeriod = new BillingPeriod(September2026, DateTimeOffset.UtcNow);
                DataStore.BillingPeriods[September2026.ToString()] = billingPeriod;
            }

            Service = new CorporateBillingDraftService(
                new InMemoryBillingPeriodRepository(DataStore),
                new InMemoryCompanyRepository(DataStore),
                new InMemoryCorporateMemberRepository(DataStore),
                new InMemoryBillingDraftRepository(DataStore),
                new InMemoryAuditLogRepository(DataStore));
        }

        public InMemoryBillingDataStore DataStore { get; }

        public CorporateBillingDraftService Service { get; }

        public Task<Contracts.GenerateCorporateBillingDraftsResponse> GenerateAsync()
        {
            return Service.GenerateAsync(September2026, OperatorId, CancellationToken.None);
        }

        public void AddCompany(
            string taxId,
            string name,
            decimal? amountPerMember,
            bool isActive = true)
        {
            var company = Company.CreateManually(taxId, name, OperatorId, DateTimeOffset.UtcNow);
            if (amountPerMember is not null)
            {
                company.SetAmountPerMember(amountPerMember, OperatorId, DateTimeOffset.UtcNow);
            }

            if (!isActive)
            {
                company.Deactivate(OperatorId, DateTimeOffset.UtcNow);
            }

            DataStore.Companies[taxId] = company;
        }

        public void AddMembers(string companyTaxId, int count)
        {
            for (var index = 0; index < count; index++)
            {
                AddMember(companyTaxId, "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO");
            }
        }

        public void AddMember(string companyTaxId, string contractName, bool isActive = true)
        {
            var memberId = nextMemberId++;
            var member = CorporateMember.Create(
                memberId,
                $"Colaborador {memberId}",
                companyTaxId,
                [new CorporateMemberContract($"c{memberId}", null, contractName)],
                Guid.NewGuid(),
                OperatorId,
                DateTimeOffset.UtcNow);
            if (!isActive)
            {
                member.Deactivate(Guid.NewGuid(), OperatorId, DateTimeOffset.UtcNow);
            }

            DataStore.CorporateMembers[memberId] = member;
        }
    }
}
