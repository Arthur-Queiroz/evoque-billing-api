namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Identidade estável de uma empresa no produto: o CNPJ somente com dígitos.
/// A validação verifica os dois dígitos verificadores, porque uma sequência
/// qualquer de 14 dígitos aceitaria empresas inexistentes no catálogo.
/// </summary>
public static class CompanyTaxId
{
    private const int TaxIdLength = 14;
    private static readonly int[] FirstCheckDigitWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondCheckDigitWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static bool TryNormalize(string? value, out string normalizedTaxId)
    {
        normalizedTaxId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digitsOnly = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digitsOnly.Length != TaxIdLength)
        {
            return false;
        }

        if (digitsOnly.Distinct().Count() == 1)
        {
            return false;
        }

        if (!HasValidCheckDigits(digitsOnly))
        {
            return false;
        }

        normalizedTaxId = digitsOnly;
        return true;
    }

    public static string Normalize(string? value)
    {
        if (!TryNormalize(value, out var normalizedTaxId))
        {
            throw new ValidationException(
                "O CNPJ informado é inválido. Informe 14 dígitos com dígitos verificadores válidos.");
        }

        return normalizedTaxId;
    }

    /// <summary>Formata para exibição no padrão 00.000.000/0000-00.</summary>
    public static string Format(string normalizedTaxId)
    {
        if (normalizedTaxId.Length != TaxIdLength)
        {
            return normalizedTaxId;
        }

        return string.Concat(
            normalizedTaxId[..2],
            ".",
            normalizedTaxId[2..5],
            ".",
            normalizedTaxId[5..8],
            "/",
            normalizedTaxId[8..12],
            "-",
            normalizedTaxId[12..]);
    }

    private static bool HasValidCheckDigits(string digitsOnly)
    {
        var firstCheckDigit = CalculateCheckDigit(digitsOnly[..12], FirstCheckDigitWeights);
        if (firstCheckDigit != digitsOnly[12] - '0')
        {
            return false;
        }

        var secondCheckDigit = CalculateCheckDigit(digitsOnly[..13], SecondCheckDigitWeights);
        return secondCheckDigit == digitsOnly[13] - '0';
    }

    private static int CalculateCheckDigit(string digits, int[] weights)
    {
        var weightedSum = 0;
        for (var position = 0; position < digits.Length; position++)
        {
            weightedSum += (digits[position] - '0') * weights[position];
        }

        var remainder = weightedSum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
