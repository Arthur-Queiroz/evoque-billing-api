using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class CompanyAsaasSynchronizationServiceTests
{
    private const string CompanyTaxId = "56087276000103";
    private const string OperatorId = "operador@evoque";

    [Fact]
    public async Task SynchronizeSandboxAsync_CreatesAndPersistsATestCustomerWhenMissing()
    {
        var scenario = CreateScenario(AsaasCustomerLookupResult.NotFound());

        var response = await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br", OperatorId),
            CancellationToken.None);

        Assert.True(response.CreatedNow);
        Assert.Equal("Linked", response.Status);
        Assert.Equal("cus_sandbox_created", response.CustomerId);
        Assert.Equal(1, scenario.Gateway.CreateSandboxCallCount);
        Assert.Equal(
            "cus_sandbox_created",
            scenario.DataStore.Companies[CompanyTaxId].AsaasSandboxCustomerId);
        Assert.Null(scenario.DataStore.Companies[CompanyTaxId].AsaasProductionCustomerId);
    }

    [Fact]
    public async Task SynchronizeSandboxAsync_ReusesTheCustomerFoundByTaxId()
    {
        var existingCustomer = new AsaasCustomer(
            "cus_sandbox_existing",
            "Open Sports",
            CompanyTaxId,
            "teste@evoque.com.br",
            null);
        var scenario = CreateScenario(AsaasCustomerLookupResult.Found(existingCustomer));

        var response = await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br", OperatorId),
            CancellationToken.None);

        Assert.False(response.CreatedNow);
        Assert.Equal("cus_sandbox_existing", response.CustomerId);
        Assert.Equal(0, scenario.Gateway.CreateSandboxCallCount);
        Assert.Equal(
            "cus_sandbox_existing",
            scenario.DataStore.Companies[CompanyTaxId].AsaasSandboxCustomerId);
    }

    [Fact]
    public async Task SynchronizeProductionAsync_LinksTheExistingCustomerWithoutCreatingAnything()
    {
        var productionCustomer = new AsaasCustomer(
            "cus_production",
            "Open Sports",
            CompanyTaxId,
            "financeiro@empresa.com.br",
            null);
        var scenario = CreateScenario(AsaasCustomerLookupResult.Found(productionCustomer));

        var response = await scenario.Service.SynchronizeProductionAsync(
            CompanyTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);

        Assert.Equal("Linked", response.Status);
        Assert.False(response.CreatedNow);
        Assert.Equal(0, scenario.Gateway.CreateSandboxCallCount);
        Assert.Equal(
            "cus_production",
            scenario.DataStore.Companies[CompanyTaxId].AsaasProductionCustomerId);
    }

    [Theory]
    [InlineData(AsaasCustomerLookupStatus.NotFound, "NotFound")]
    [InlineData(AsaasCustomerLookupStatus.Ambiguous, "Ambiguous")]
    public async Task SynchronizeProductionAsync_DoesNotCreateOrLinkWhenLookupIsNotUnique(
        AsaasCustomerLookupStatus lookupStatus,
        string expectedStatus)
    {
        var lookupResult = lookupStatus == AsaasCustomerLookupStatus.NotFound
            ? AsaasCustomerLookupResult.NotFound()
            : AsaasCustomerLookupResult.Ambiguous(2);
        var scenario = CreateScenario(lookupResult);

        var response = await scenario.Service.SynchronizeProductionAsync(
            CompanyTaxId,
            new CompanyOperatorRequest(OperatorId),
            CancellationToken.None);

        Assert.Equal(expectedStatus, response.Status);
        Assert.Equal(0, scenario.Gateway.CreateSandboxCallCount);
        Assert.Null(scenario.DataStore.Companies[CompanyTaxId].AsaasProductionCustomerId);
    }

    private static TestScenario CreateScenario(AsaasCustomerLookupResult lookupResult)
    {
        var dataStore = new InMemoryBillingDataStore();
        var companyRepository = new InMemoryCompanyRepository(dataStore);
        var company = Company.CreateManually(
            CompanyTaxId,
            "Open Sports",
            OperatorId,
            DateTimeOffset.UtcNow);
        dataStore.Companies[CompanyTaxId] = company;
        var gateway = new StubAsaasCustomerGateway(lookupResult);
        return new TestScenario(
            new CompanyAsaasSynchronizationService(
                companyRepository,
                gateway,
                new InMemoryAuditLogRepository(dataStore)),
            gateway,
            dataStore);
    }

    private sealed record TestScenario(
        CompanyAsaasSynchronizationService Service,
        StubAsaasCustomerGateway Gateway,
        InMemoryBillingDataStore DataStore);

    private sealed class StubAsaasCustomerGateway(AsaasCustomerLookupResult lookupResult)
        : IAsaasCustomerGateway
    {
        public int CreateSandboxCallCount { get; private set; }

        public Task<AsaasCustomerPage> ListAsync(
            string? searchTerm,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasCustomerPage([], false, 0));
        }

        public Task<AsaasCustomerLookupResult> FindByTaxIdAsync(
            AsaasEnvironment asaasEnvironment,
            string taxId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(lookupResult);
        }

        public Task<AsaasCustomer> CreateSandboxAsync(
            string name,
            string taxId,
            string email,
            CancellationToken cancellationToken)
        {
            CreateSandboxCallCount++;
            return Task.FromResult(
                new AsaasCustomer(
                    "cus_sandbox_created",
                    name,
                    taxId,
                    email,
                    null));
        }
    }
}
