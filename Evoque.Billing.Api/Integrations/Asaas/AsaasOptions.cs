using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public sealed class AsaasOptions
{
    public const string SectionName = "Asaas";

    public string IntegrationEnvironment { get; init; } = "Sandbox";

    public string BaseUrl { get; init; } = "https://api-sandbox.asaas.com/v3/";

    public string ApiKey { get; init; } = string.Empty;

    public bool AllowChargeCreation { get; init; }

    public AsaasConnectionOptions Sandbox { get; init; } = new();

    public AsaasConnectionOptions Production { get; init; } = new();

    public AsaasConnectionOptions GetConnection(AsaasEnvironment asaasEnvironment)
    {
        var selectedConnection = asaasEnvironment == AsaasEnvironment.Sandbox ? Sandbox : Production;
        if (selectedConnection.HasApiKey())
        {
            return selectedConnection;
        }

        if (Enum.TryParse<AsaasEnvironment>(IntegrationEnvironment, true, out var legacyEnvironment)
            && legacyEnvironment == asaasEnvironment)
        {
            return new AsaasConnectionOptions
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                AllowChargeCreation = AllowChargeCreation,
            };
        }

        throw new ExternalOperationNotAllowedException(
            $"O ambiente Asaas {asaasEnvironment} não possui credenciais configuradas.");
    }

    public bool IsConfigured(AsaasEnvironment asaasEnvironment)
    {
        var selectedConnection = asaasEnvironment == AsaasEnvironment.Sandbox ? Sandbox : Production;
        if (selectedConnection.HasApiKey())
        {
            return true;
        }

        return Enum.TryParse<AsaasEnvironment>(IntegrationEnvironment, true, out var legacyEnvironment)
            && legacyEnvironment == asaasEnvironment
            && !string.IsNullOrWhiteSpace(ApiKey);
    }

    public bool CanCreateCharges(AsaasEnvironment asaasEnvironment)
    {
        if (!IsConfigured(asaasEnvironment))
        {
            return false;
        }

        return GetConnection(asaasEnvironment).AllowChargeCreation;
    }
}

public sealed class AsaasConnectionOptions
{
    public string BaseUrl { get; init; } = "https://api-sandbox.asaas.com/v3/";

    public string ApiKey { get; init; } = string.Empty;

    public bool AllowChargeCreation { get; init; }

    public bool HasApiKey() => !string.IsNullOrWhiteSpace(ApiKey);
}
