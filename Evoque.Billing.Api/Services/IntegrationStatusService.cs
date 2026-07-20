using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Integrations.Evo;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Services;

public sealed class IntegrationStatusService(
    IOptions<AsaasOptions> asaasOptions,
    IOptions<EvoOptions> evoOptions)
{
    public IntegrationStatusResponse GetStatus()
    {
        var configuredEvoOptions = evoOptions.Value;
        var evoIsConfigured = !string.IsNullOrWhiteSpace(configuredEvoOptions.BaseUrl)
            && !string.IsNullOrWhiteSpace(configuredEvoOptions.Username)
            && !string.IsNullOrWhiteSpace(configuredEvoOptions.ApiKey);

        var configuredAsaasOptions = asaasOptions.Value;
        return new IntegrationStatusResponse(
            configuredAsaasOptions.IntegrationEnvironment,
            configuredAsaasOptions.AllowChargeCreation,
            new AsaasEnvironmentStatusResponse(
                AsaasEnvironment.Sandbox.ToString(),
                configuredAsaasOptions.IsConfigured(AsaasEnvironment.Sandbox),
                configuredAsaasOptions.CanCreateCharges(AsaasEnvironment.Sandbox)),
            new AsaasEnvironmentStatusResponse(
                AsaasEnvironment.Production.ToString(),
                configuredAsaasOptions.IsConfigured(AsaasEnvironment.Production),
                configuredAsaasOptions.CanCreateCharges(AsaasEnvironment.Production)),
            evoIsConfigured,
            evoIsConfigured
                ? "Integração Evo configurada."
                : "Aguardando URL, credencial e documentação da API do Evo.");
    }
}
