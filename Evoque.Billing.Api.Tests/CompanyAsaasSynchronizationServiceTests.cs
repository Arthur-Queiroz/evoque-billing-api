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
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br"),
            OperatorId,
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
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br"),
            OperatorId,
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
            OperatorId,
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
            OperatorId,
            CancellationToken.None);

        Assert.Equal(expectedStatus, response.Status);
        Assert.Equal(0, scenario.Gateway.CreateSandboxCallCount);
        Assert.Null(scenario.DataStore.Companies[CompanyTaxId].AsaasProductionCustomerId);
    }

    /// <summary>
    /// O Asaas recusa a nota fiscal quando o tomador não tem endereço completo.
    /// O catálogo já recebe esse endereço da BrasilAPI, então o espelho de teste
    /// nasce com ele — é o que torna a simulação fiel ao que produção fará.
    /// </summary>
    [Fact]
    public async Task SynchronizeSandboxAsync_SendsTheCatalogAddressWhenCreatingTheMirror()
    {
        var scenario = CreateScenario(AsaasCustomerLookupResult.NotFound());
        var company = scenario.DataStore.Companies[CompanyTaxId];
        company.ApplyRegistryData(
            "OPEN SPORTS LTDA",
            "Open Sports",
            "ATIVA",
            new CompanyRegistryAddress(
                "Rua Silva Jardim",
                "270",
                "Sala 4",
                "Centro",
                "Santo André",
                "SP",
                "09190370"),
            DateTimeOffset.UtcNow);

        await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br"),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, scenario.Gateway.CreateSandboxCallCount);
        var endereco = scenario.Gateway.LastSandboxAddress;
        Assert.NotNull(endereco);
        Assert.Equal("Rua Silva Jardim", endereco.Street);
        Assert.Equal("270", endereco.Number);
        Assert.Equal("Centro", endereco.Neighborhood);
        Assert.Equal("09190370", endereco.PostalCode);
    }

    /// <summary>
    /// Uma empresa sem endereço no catálogo continua tendo o espelho criado. A
    /// nota dela falhará com a mensagem do Asaas, que a tela exibe — é melhor
    /// expor a lacuna do que escondê-la com um endereço inventado.
    /// </summary>
    [Fact]
    public async Task SynchronizeSandboxAsync_StillCreatesTheMirrorWithoutACatalogAddress()
    {
        var scenario = CreateScenario(AsaasCustomerLookupResult.NotFound());

        var resultado = await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br"),
            OperatorId,
            CancellationToken.None);

        Assert.Equal(1, scenario.Gateway.CreateSandboxCallCount);
        Assert.Null(scenario.Gateway.LastSandboxAddress);
        Assert.Equal("Linked", resultado.Status);
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

        public CompanyRegistryAddress? LastSandboxAddress { get; private set; }

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
            CompanyRegistryAddress? registryAddress,
            CancellationToken cancellationToken)
        {
            CreateSandboxCallCount++;
            LastSandboxAddress = registryAddress;
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
