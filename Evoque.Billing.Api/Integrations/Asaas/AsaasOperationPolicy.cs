using System.Net.Http.Headers;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Integrations.Asaas;

public static class AsaasOperationPolicy
{
    public static void ValidateReadOperation(
        IHostEnvironment hostEnvironment,
        AsaasEnvironment asaasEnvironment,
        AsaasConnectionOptions connectionOptions)
    {
        ValidateConfiguredEnvironment(asaasEnvironment, connectionOptions);

        if (!hostEnvironment.IsProduction()
            && asaasEnvironment != AsaasEnvironment.Sandbox)
        {
            throw new ExternalOperationNotAllowedException(
                "Chamadas ao Asaas fora de produção são permitidas somente no ambiente Sandbox.");
        }
    }

    public static void ValidateChargeCreation(
        IHostEnvironment hostEnvironment,
        AsaasEnvironment asaasEnvironment,
        AsaasConnectionOptions connectionOptions)
    {
        ValidateReadOperation(hostEnvironment, asaasEnvironment, connectionOptions);

        if (!connectionOptions.AllowChargeCreation)
        {
            throw new ExternalOperationNotAllowedException(
                "A criação de cobranças no Asaas está desabilitada pela configuração.");
        }
    }

    public static void ConfigureHttpClient(HttpClient httpClient, AsaasConnectionOptions connectionOptions)
    {
        if (string.IsNullOrWhiteSpace(connectionOptions.ApiKey))
        {
            throw new ExternalOperationNotAllowedException("A chave da API do Asaas não está configurada.");
        }

        httpClient.BaseAddress = new Uri(connectionOptions.BaseUrl, UriKind.Absolute);
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("access_token", connectionOptions.ApiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EvoqueBilling/0.1");
    }

    private static void ValidateConfiguredEnvironment(
        AsaasEnvironment asaasEnvironment,
        AsaasConnectionOptions connectionOptions)
    {
        if (!Uri.TryCreate(connectionOptions.BaseUrl, UriKind.Absolute, out var asaasBaseUri)
            || asaasBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ExternalOperationNotAllowedException("A URL do Asaas precisa ser HTTPS e absoluta.");
        }

        if (asaasEnvironment == AsaasEnvironment.Sandbox
            && string.Equals(asaasBaseUri.Host, "api-sandbox.asaas.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (asaasEnvironment == AsaasEnvironment.Production
            && string.Equals(asaasBaseUri.Host, "api.asaas.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ExternalOperationNotAllowedException(
            "O ambiente e a URL do Asaas estão incompatíveis ou são inválidos.");
    }
}
