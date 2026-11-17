using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Evo;
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
    public async Task GenerateAsync_CreatesANewVersionAfterThePreviousDraftWasCancelled()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        await scenario.GenerateAsync();
        var originalBillingDraft = scenario.DataStore.BillingDrafts.Single().Value;
        originalBillingDraft.Cancel(
            OperatorId,
            "Roster da competência foi atualizado.",
            DateTimeOffset.UtcNow);

        var secondResult = await scenario.GenerateAsync();

        Assert.Single(secondResult.Created);
        var drafts = scenario.DataStore.BillingDrafts.Values.OrderBy(draft => draft.Version).ToArray();
        Assert.Equal(2, drafts.Length);
        Assert.Equal(BillingDraftStatus.Cancelled, drafts[0].Status);
        Assert.Equal(2, drafts[1].Version);
        Assert.Equal(BillingDraftStatus.PendingReview, drafts[1].Status);
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

    // Setembro/2026 com fechamento no dia 25: o período vai de 26/08 a 25/09.

    [Fact]
    public async Task GenerateAsync_ChargesOnlyTheDaysUsedByAMemberWhoJoinedMidPeriod()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberRenewingMonthlySince(OpenSportsTaxId, new DateOnly(2024, 4, 22), new DateOnly(2026, 9, 22));
        var newMemberId = scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 9, 14)));

        var result = await scenario.GenerateAsync();

        // 89,90 / 30 x 11 dias (14/09 a 25/09) = 32,963...
        var draft = result.Created.Single();
        Assert.Equal(89.90m + 32.96m, draft.TotalAmount);
        var prorated = Assert.Single(draft.ProratedMembers);
        Assert.Equal(newMemberId, prorated.EvoMemberId);
        Assert.Equal(11, prorated.BilledDays);
        Assert.Equal(new DateOnly(2026, 9, 14), prorated.EntryDate);
        var item = scenario.DataStore.BillingDrafts.Single().Value.Items
            .Single(draftItem => draftItem.ExternalMemberId == newMemberId.ToString());
        Assert.Contains("proporcional 11/30 dias", item.Description, StringComparison.Ordinal);
        Assert.Empty(result.ProrationWarnings);
    }

    [Fact]
    public async Task GenerateAsync_UsesTheFirstCorporateContractAndNotTheMonthlyRenewal()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        // O contrato ativo começou em 14/09, mas é só a renovação do mês.
        scenario.AddMemberRenewingMonthlySince(OpenSportsTaxId, new DateOnly(2025, 11, 14), new DateOnly(2026, 9, 14));

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
        Assert.Empty(result.Created.Single().ProratedMembers);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFullValueToAMemberWhoLeftAndCameBack()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA", new DateOnly(2026, 3, 18)),
            ("EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA", new DateOnly(2026, 4, 18)),
            ("EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA", new DateOnly(2026, 9, 10)));

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
    }

    [Fact]
    public async Task GenerateAsync_IgnoresAPreviousSelfPaidPlanWhenFindingTheEntryDate()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOPASS RECORRENTE 79,90", new DateOnly(2023, 1, 5)),
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 9, 15)));

        var result = await scenario.GenerateAsync();

        // Aluno desde 2023, mas no convênio desde 15/09: 10 dias.
        Assert.Equal(30m, result.Created.Single().TotalAmount);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFullValueToAMemberWhoJoinedOnTheFirstDayOfThePeriod()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 8, 26)));

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
    }

    [Fact]
    public async Task GenerateAsync_LeavesOutAndReportsAMemberWhoJoinedAfterTheClosing()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberRenewingMonthlySince(OpenSportsTaxId, new DateOnly(2024, 4, 22), new DateOnly(2026, 9, 22));
        var lateMemberId = scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 10, 2)));

        var result = await scenario.GenerateAsync();

        Assert.Equal(1, result.Created.Single().MemberCount);
        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
        var outside = Assert.Single(result.MembersOutsidePeriod);
        Assert.Equal(lateMemberId, outside.EvoMemberId);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFullValueAndWarnsWhenTheCompanyHasNoClosingDay()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 9, 14)));

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
        var warning = Assert.Single(result.ProrationWarnings);
        Assert.Null(warning.EvoMemberId);
        Assert.Contains("fechamento", warning.Message, StringComparison.Ordinal);
        Assert.Empty(scenario.EvoDirectoryGateway.RequestedBatchSizes);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFullValueAndWarnsWhenEvoIsUnavailable()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMemberWithHistory(
            OpenSportsTaxId,
            ("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", new DateOnly(2026, 9, 14)));
        scenario.EvoDirectoryGateway.IsUnavailable = true;

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
        var warning = Assert.Single(result.ProrationWarnings);
        Assert.Contains("Evo indisponível", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_ChargesTheFullValueAndWarnsWhenEvoHasNoCorporateContract()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMembers(OpenSportsTaxId, 1);

        var result = await scenario.GenerateAsync();

        Assert.Equal(89.90m, result.Created.Single().TotalAmount);
        Assert.Contains("não encontrado", Assert.Single(result.ProrationWarnings).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_QueriesEvoInBatchesOfFifty()
    {
        var scenario = new TestScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.SetClosingDay(OpenSportsTaxId, 25);
        scenario.AddMembers(OpenSportsTaxId, 120);

        await scenario.GenerateAsync();

        Assert.Equal([50, 50, 20], scenario.EvoDirectoryGateway.RequestedBatchSizes);
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
                new InMemoryAuditLogRepository(DataStore),
                new InMemoryCompanyBillingScheduleRepository(DataStore),
                new CorporateMemberEntryDateService(EvoDirectoryGateway));
        }

        public InMemoryBillingDataStore DataStore { get; }

        public EntryDateEvoDirectoryGateway EvoDirectoryGateway { get; } = new();

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

        public void SetClosingDay(string companyTaxId, int closingDay)
        {
            DataStore.CompanyBillingSchedules[companyTaxId] = CompanyBillingSchedule.Create(
                companyTaxId,
                closingDay,
                isActive: true,
                OperatorId,
                DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Um colaborador com o histórico de contratos que o EVO devolveria.
        /// </summary>
        public long AddMemberWithHistory(
            string companyTaxId,
            params (string ContractName, DateOnly StartDate)[] contractHistory)
        {
            var memberId = nextMemberId;
            AddMember(companyTaxId, "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO");
            EvoDirectoryGateway.Members[memberId] = new EvoMember(
                (int)memberId,
                1,
                "Filial",
                $"Colaborador {memberId}",
                null,
                contractHistory
                    .Select((contract, index) => new EvoMembership(
                        1361,
                        index + 1,
                        contract.ContractName,
                        "expired",
                        null,
                        null,
                        new DateTimeOffset(contract.StartDate.ToDateTime(TimeOnly.MinValue)),
                        new DateTimeOffset(contract.StartDate.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue)),
                        null))
                    .ToArray());
            return memberId;
        }

        /// <summary>
        /// Renovação mensal, como o EVO faz com o contrato corporativo.
        /// </summary>
        public long AddMemberRenewingMonthlySince(string companyTaxId, DateOnly firstStartDate, DateOnly until)
        {
            var history = new List<(string, DateOnly)>();
            for (var startDate = firstStartDate; startDate <= until; startDate = startDate.AddMonths(1))
            {
                history.Add(("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", startDate));
            }

            return AddMemberWithHistory(companyTaxId, history.ToArray());
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

    private sealed class EntryDateEvoDirectoryGateway : IEvoDirectoryGateway
    {
        public Dictionary<long, EvoMember> Members { get; } = [];

        public bool IsUnavailable { get; set; }

        public List<int> RequestedBatchSizes { get; } = [];

        public Task<IReadOnlyCollection<EvoMember>> ListMembersByIdsAsync(
            IReadOnlyCollection<long> memberIds,
            CancellationToken cancellationToken)
        {
            RequestedBatchSizes.Add(memberIds.Count);
            if (IsUnavailable)
            {
                throw new ExternalOperationNotAllowedException("Não foi possível consultar membros no Evo (HTTP 503).");
            }

            IReadOnlyCollection<EvoMember> found = memberIds
                .Where(Members.ContainsKey)
                .Select(memberId => Members[memberId])
                .ToArray();
            return Task.FromResult(found);
        }

        public Task<IReadOnlyCollection<EvoEmployee>> ListEmployeesAsync(
            string? name, string? email, int take, int skip, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<EvoMember>> ListMembersAsync(
            int status, int take, int skip, bool includeMemberships, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<EvoPartnership>> ListPartnershipsAsync(
            int status, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<EvoMemberMembership>> ListMemberMembershipsAsync(
            int take, int skip, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<EvoReceivable>> ListReceivablesAsync(
            DateOnly competenceDateStart, DateOnly competenceDateEnd, int take, int skip, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EvoSale> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<EvoBranchGroup>> ListBranchGroupsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
