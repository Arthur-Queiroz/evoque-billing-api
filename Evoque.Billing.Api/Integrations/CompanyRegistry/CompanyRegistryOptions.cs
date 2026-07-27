namespace Evoque.Billing.Api.Integrations.CompanyRegistry;

/// <summary>
/// Configuração da consulta ao cadastro público de CNPJ. O serviço é externo e
/// experimental, então o timeout é curto e a concorrência é pequena por decisão
/// de projeto: o catálogo precisa funcionar mesmo sem ele.
/// </summary>
public sealed class CompanyRegistryOptions
{
    public const string SectionName = "CompanyRegistry";

    public string BaseUrl { get; init; } = "https://brasilapi.com.br/api/";

    public int TimeoutSeconds { get; init; } = 8;

    /// <summary>Quantas consultas cadastrais podem acontecer ao mesmo tempo.</summary>
    public int MaximumConcurrentLookups { get; init; } = 3;
}
