using System.Net;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Evoque.Billing.Api.Domain;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Integrations.CompanyRegistry;

/// <summary>
/// Consulta `cnpj/v1/{cnpj}` na BrasilAPI. Toda falha do serviço externo vira
/// <see cref="CompanyRegistryLookupStatus.Unavailable"/> ou
/// <see cref="CompanyRegistryLookupStatus.NotFound"/>, nunca uma exceção que
/// pudesse cancelar a sincronização do catálogo.
/// </summary>
public sealed class BrasilApiCompanyRegistryGateway : ICompanyRegistryGateway
{
    private readonly HttpClient httpClient;
    private readonly ILogger<BrasilApiCompanyRegistryGateway> logger;

    public BrasilApiCompanyRegistryGateway(
        HttpClient httpClient,
        IOptions<CompanyRegistryOptions> companyRegistryOptions,
        ILogger<BrasilApiCompanyRegistryGateway> logger)
    {
        var options = companyRegistryOptions.Value;
        this.httpClient = httpClient;
        this.logger = logger;
        this.httpClient.BaseAddress = new Uri(
            options.BaseUrl.EndsWith('/') ? options.BaseUrl : $"{options.BaseUrl}/");
        this.httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }

    public async Task<CompanyRegistryLookupResult> FindByTaxIdAsync(
        string taxId,
        CancellationToken cancellationToken)
    {
        if (!CompanyTaxId.TryNormalize(taxId, out var normalizedTaxId))
        {
            return CompanyRegistryLookupResult.NotFound();
        }

        try
        {
            using var response = await httpClient.GetAsync(
                $"cnpj/v1/{normalizedTaxId}",
                cancellationToken);
            return await ReadResponseAsync(normalizedTaxId, response, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "A consulta cadastral do CNPJ {TaxId} excedeu o tempo limite.",
                normalizedTaxId);
            return CompanyRegistryLookupResult.Unavailable();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "A consulta cadastral do CNPJ {TaxId} falhou.",
                normalizedTaxId);
            return CompanyRegistryLookupResult.Unavailable();
        }
    }

    private async Task<CompanyRegistryLookupResult> ReadResponseAsync(
        string normalizedTaxId,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // `400` significa CNPJ recusado pelo cadastro público e `404` significa
        // CNPJ inexistente. Nos dois casos não há dados a persistir, e repetir a
        // consulta automaticamente não mudaria o resultado.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return CompanyRegistryLookupResult.NotFound();
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "O cadastro público limitou as consultas ao processar o CNPJ {TaxId}.",
                normalizedTaxId);
            return CompanyRegistryLookupResult.Unavailable();
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "O cadastro público respondeu {StatusCode} para o CNPJ {TaxId}.",
                (int)response.StatusCode,
                normalizedTaxId);
            return CompanyRegistryLookupResult.Unavailable();
        }

        var registryData = await response.Content.ReadFromJsonAsync<BrasilApiCompanyResponse>(
            cancellationToken: cancellationToken);
        if (registryData is null)
        {
            return CompanyRegistryLookupResult.Unavailable();
        }

        return CompanyRegistryLookupResult.Found(
            registryData.LegalName,
            registryData.TradeName,
            registryData.RegistrationStatusDescription,
            ReadAddress(registryData));
    }

    /// <summary>
    /// O cadastro público separa o tipo do logradouro do nome. Juntar os dois
    /// produz "AVENIDA PERIMETRAL NORTE" em vez de "PERIMETRAL NORTE".
    /// </summary>
    private static CompanyRegistryAddress? ReadAddress(BrasilApiCompanyResponse registryData)
    {
        if (string.IsNullOrWhiteSpace(registryData.City))
        {
            return null;
        }

        var street = string.Join(
            " ",
            new[] { registryData.StreetType, registryData.Street }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new CompanyRegistryAddress(
            street.Trim(),
            registryData.Number?.Trim() ?? string.Empty,
            registryData.Complement?.Trim() ?? string.Empty,
            registryData.Neighborhood?.Trim() ?? string.Empty,
            registryData.City.Trim(),
            registryData.State?.Trim() ?? string.Empty,
            registryData.PostalCode?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Somente os campos confirmados na resposta real da BrasilAPI. Todos são
    /// opcionais porque o serviço não garante preenchimento.
    /// </summary>
    private sealed record BrasilApiCompanyResponse
    {
        [JsonPropertyName("razao_social")]
        public string? LegalName { get; init; }

        [JsonPropertyName("nome_fantasia")]
        public string? TradeName { get; init; }

        [JsonPropertyName("descricao_situacao_cadastral")]
        public string? RegistrationStatusDescription { get; init; }

        [JsonPropertyName("descricao_tipo_de_logradouro")]
        public string? StreetType { get; init; }

        [JsonPropertyName("logradouro")]
        public string? Street { get; init; }

        [JsonPropertyName("numero")]
        public string? Number { get; init; }

        [JsonPropertyName("complemento")]
        public string? Complement { get; init; }

        [JsonPropertyName("bairro")]
        public string? Neighborhood { get; init; }

        [JsonPropertyName("municipio")]
        public string? City { get; init; }

        [JsonPropertyName("uf")]
        public string? State { get; init; }

        [JsonPropertyName("cep")]
        public string? PostalCode { get; init; }
    }
}
