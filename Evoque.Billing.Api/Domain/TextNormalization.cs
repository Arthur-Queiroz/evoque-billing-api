using System.Globalization;
using System.Text;

namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Comparação de texto sem acento e sem diferenciar maiúsculas, usada para
/// reconhecer cabeçalho de planilha, deduplicar contrato, buscar colaborador
/// ou empresa por nome, e buscar o histórico de emissões. Nenhum desses usos é
/// específico de planilha — daí o nome, em vez de `SpreadsheetText`.
/// </summary>
public static class TextNormalization
{
    public static string Normalize(string value)
    {
        var normalizedValue = value.Trim().Normalize(NormalizationForm.FormD);
        var characters = normalizedValue
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(characters).Normalize(NormalizationForm.FormC);
    }
}
