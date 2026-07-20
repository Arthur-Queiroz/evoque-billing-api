namespace Evoque.Billing.Api.Contracts;

public sealed record IntegrationStatusResponse(
    string AsaasEnvironment,
    bool AsaasChargeCreationEnabled,
    AsaasEnvironmentStatusResponse Sandbox,
    AsaasEnvironmentStatusResponse Production,
    bool EvoIsConfigured,
    string EvoMessage);

public sealed record AsaasEnvironmentStatusResponse(
    string Environment,
    bool IsConfigured,
    bool ChargeCreationEnabled);
