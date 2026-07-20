using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Evoque.Billing.Api.Tests;

public sealed class AsaasOperationPolicyTests
{
    [Fact]
    public void ValidateReadOperation_BlocksProductionAsaasOutsideProductionHost()
    {
        var connectionOptions = new AsaasConnectionOptions
        {
            BaseUrl = "https://api.asaas.com/v3/",
        };

        Assert.Throws<ExternalOperationNotAllowedException>(() =>
            AsaasOperationPolicy.ValidateReadOperation(
                new TestHostEnvironment(Environments.Development),
                AsaasEnvironment.Production,
                connectionOptions));
    }

    [Fact]
    public void ValidateReadOperation_AllowsSandboxOutsideProductionHost()
    {
        var connectionOptions = new AsaasConnectionOptions
        {
            BaseUrl = "https://api-sandbox.asaas.com/v3/",
        };

        AsaasOperationPolicy.ValidateReadOperation(
            new TestHostEnvironment(Environments.Development),
            AsaasEnvironment.Sandbox,
            connectionOptions);
    }

    [Fact]
    public void ValidateChargeCreation_RequiresExplicitEnablement()
    {
        var connectionOptions = new AsaasConnectionOptions
        {
            BaseUrl = "https://api-sandbox.asaas.com/v3/",
            AllowChargeCreation = false,
        };

        Assert.Throws<ExternalOperationNotAllowedException>(() =>
            AsaasOperationPolicy.ValidateChargeCreation(
                new TestHostEnvironment(Environments.Development),
                AsaasEnvironment.Sandbox,
                connectionOptions));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Evoque.Billing.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
