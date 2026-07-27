using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;

namespace Evoque.Billing.Api.Tests;

public sealed class AsaasOptionsTests
{
    [Fact]
    public void GetConnection_UsesIndependentProductionCredentials()
    {
        var options = CreateOptionsWithReadOnlyProduction();

        var productionConnection = options.GetConnection(AsaasEnvironment.Production);

        Assert.Equal("https://api.asaas.com/v3/", productionConnection.BaseUrl);
        Assert.Equal("production-token", productionConnection.ApiKey);
    }

    [Fact]
    public void CanCreateCharges_KeepsConfiguredProductionReadOnly()
    {
        var options = CreateOptionsWithReadOnlyProduction();

        Assert.True(options.IsConfigured(AsaasEnvironment.Production));
        Assert.False(options.CanCreateCharges(AsaasEnvironment.Production));
    }

    private static AsaasOptions CreateOptionsWithReadOnlyProduction()
    {
        return new AsaasOptions
        {
            IntegrationEnvironment = AsaasEnvironment.Sandbox.ToString(),
            BaseUrl = "https://api-sandbox.asaas.com/v3/",
            ApiKey = "sandbox-token",
            AllowChargeCreation = true,
            Production = new AsaasConnectionOptions
            {
                BaseUrl = "https://api.asaas.com/v3/",
                ApiKey = "production-token",
                AllowChargeCreation = false,
            },
        };
    }
}
