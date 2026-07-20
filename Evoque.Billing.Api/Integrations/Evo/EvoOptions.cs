namespace Evoque.Billing.Api.Integrations.Evo;

public sealed class EvoOptions
{
    public const string SectionName = "Evo";

    public string BaseUrl { get; init; } = "https://evo-integracao-api.w12app.com.br/";

    public string Username { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}
