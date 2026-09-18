using Evoque.Billing.Api.Authentication;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Integrations.Evo;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Services;

public sealed class StartupConfigurationValidator(
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    IOptions<AsaasOptions> asaasOptions,
    IOptions<EvoOptions> evoOptions,
    IOptions<FiscalInvoiceOptions> fiscalInvoiceOptions,
    IOptions<OperatorAccountOptions> operatorAccountOptions)
{
    public void Validate()
    {
        // Primeiro de tudo: sem operador configurado o sistema subiria aberto, e
        // é o único erro aqui cuja consequência é pior que não subir.
        operatorAccountOptions.Value.Validate();

        var configuredAsaasOptions = asaasOptions.Value;
        if (!Uri.TryCreate(configuredAsaasOptions.BaseUrl, UriKind.Absolute, out var asaasBaseUri)
            || asaasBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Asaas:BaseUrl deve ser uma URL HTTPS absoluta.");
        }

        if (!string.Equals(configuredAsaasOptions.IntegrationEnvironment, "Sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configuredAsaasOptions.IntegrationEnvironment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Asaas:IntegrationEnvironment deve ser Sandbox ou Production.");
        }

        var expectedAsaasHost = string.Equals(
            configuredAsaasOptions.IntegrationEnvironment,
            "Sandbox",
            StringComparison.OrdinalIgnoreCase)
            ? "api-sandbox.asaas.com"
            : "api.asaas.com";
        if (!string.Equals(asaasBaseUri.Host, expectedAsaasHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Asaas:BaseUrl não corresponde ao ambiente Asaas configurado.");
        }

        if (configuredAsaasOptions.AllowChargeCreation
            && string.IsNullOrWhiteSpace(configuredAsaasOptions.ApiKey))
        {
            throw new InvalidOperationException(
                "Asaas:ApiKey é obrigatório quando a criação de cobranças estiver habilitada.");
        }

        var invoiceIssuanceIsEnabledSomewhere = configuredAsaasOptions.AllowInvoiceIssuance
            || configuredAsaasOptions.CanIssueInvoices(AsaasEnvironment.Sandbox)
            || configuredAsaasOptions.CanIssueInvoices(AsaasEnvironment.Production);
        if (invoiceIssuanceIsEnabledSomewhere && !fiscalInvoiceOptions.Value.IsComplete())
        {
            throw new InvalidOperationException(
                "FiscalInvoice:MunicipalServiceId e FiscalInvoice:IssTaxRate são obrigatórios "
                + "quando a emissão de notas fiscais estiver habilitada.");
        }

        ValidateEvoConfiguration(evoOptions.Value);

        if (!hostEnvironment.IsProduction())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("BillingDatabase")))
        {
            throw new InvalidOperationException("A connection string BillingDatabase é obrigatória em produção.");
        }

        if (string.IsNullOrWhiteSpace(configuredAsaasOptions.ApiKey))
        {
            throw new InvalidOperationException("Asaas:ApiKey é obrigatório em produção.");
        }

        var allowedCorsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (allowedCorsOrigins.Any(IsNotHttpsOrigin))
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins deve conter somente origens HTTPS quando configurado em produção.");
        }
    }

    private static bool IsNotHttpsOrigin(string origin)
    {
        return !Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || originUri.Scheme != Uri.UriSchemeHttps;
    }

    private static void ValidateEvoConfiguration(EvoOptions options)
    {
        var credentialsWereConfigured = !string.IsNullOrWhiteSpace(options.Username)
            || !string.IsNullOrWhiteSpace(options.ApiKey);
        if (!credentialsWereConfigured)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Evo:Username e Evo:ApiKey precisam ser configurados juntos.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var evoBaseUri)
            || evoBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Evo:BaseUrl deve ser uma URL HTTPS absoluta.");
        }
    }
}
