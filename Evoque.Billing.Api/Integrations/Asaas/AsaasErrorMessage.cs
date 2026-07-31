using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Evoque.Billing.Api.Integrations.Asaas;

/// <summary>
/// Lê o motivo que o Asaas devolve no corpo de uma resposta de erro.
///
/// Sem isso, uma recusa chegava ao operador como "HTTP 400" e escondia a única
/// informação útil da resposta. Um lote falhou por vencimento no passado e a
/// tela não tinha como dizer isso: o corpo trazia a explicação e era descartado.
///
/// O formato do Asaas é <c>{"errors":[{"code":"...","description":"..."}]}</c>.
/// Quando o corpo não é esse JSON — um HTML de proxy, por exemplo — sobra o
/// código HTTP, que já é melhor do que nada.
/// </summary>
public static class AsaasErrorMessage
{
    private const int MaximumReportedLength = 400;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<string> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? responseBody = null;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Um corpo ilegível não pode transformar a recusa do Asaas em uma
            // falha diferente: o código HTTP ainda descreve o que aconteceu.
        }

        return Describe(response.StatusCode, responseBody);
    }

    public static string Describe(HttpStatusCode statusCode, string? responseBody)
    {
        var reportedReason = ExtractDescriptions(responseBody);
        return string.IsNullOrWhiteSpace(reportedReason)
            ? $"HTTP {(int)statusCode}"
            : $"{reportedReason} (HTTP {(int)statusCode})";
    }

    private static string? ExtractDescriptions(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        AsaasFailureResponse? failureResponse;
        try
        {
            failureResponse = JsonSerializer.Deserialize<AsaasFailureResponse>(responseBody, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        var descriptions = failureResponse?.Errors
            ?.Select(error => error.Description)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description!.Trim())
            .ToArray();
        if (descriptions is null || descriptions.Length == 0)
        {
            return null;
        }

        return Truncate(string.Join(" ", descriptions));
    }

    private static string Truncate(string reportedReason)
    {
        var singleLine = reportedReason.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= MaximumReportedLength
            ? singleLine
            : singleLine[..MaximumReportedLength] + "…";
    }

    private sealed record AsaasFailureResponse(
        [property: JsonPropertyName("errors")] IReadOnlyCollection<AsaasFailureDetail>? Errors);

    private sealed record AsaasFailureDetail(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("description")] string? Description);
}
