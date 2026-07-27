using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class CompanyTaxIdTests
{
    [Theory]
    [InlineData("56087276000103")]
    [InlineData("56.087.276/0001-03")]
    [InlineData(" 56.087.276/0001-03 ")]
    [InlineData("43322169000170")]
    [InlineData("43.322.169/0001-70")]
    public void TryNormalize_AcceptsValidTaxIdWithOrWithoutFormatting(string taxId)
    {
        var wasNormalized = CompanyTaxId.TryNormalize(taxId, out var normalizedTaxId);

        Assert.True(wasNormalized);
        Assert.Equal(14, normalizedTaxId.Length);
        Assert.All(normalizedTaxId, character => Assert.True(char.IsAsciiDigit(character)));
    }

    [Theory]
    [InlineData("56087276000104")] // Segundo dígito verificador incorreto.
    [InlineData("56087276000113")] // Primeiro dígito verificador incorreto.
    [InlineData("12345678000199")]
    public void TryNormalize_RejectsFourteenDigitsWithInvalidCheckDigits(string taxId)
    {
        var wasNormalized = CompanyTaxId.TryNormalize(taxId, out var normalizedTaxId);

        Assert.False(wasNormalized);
        Assert.Empty(normalizedTaxId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("5608727600010")]
    [InlineData("560872760001033")]
    [InlineData("11111111111111")]
    public void TryNormalize_RejectsMalformedTaxId(string? taxId)
    {
        Assert.False(CompanyTaxId.TryNormalize(taxId, out _));
    }

    [Fact]
    public void Normalize_ThrowsValidationExceptionForInvalidTaxId()
    {
        Assert.Throws<ValidationException>(() => CompanyTaxId.Normalize("12345678000199"));
    }

    [Fact]
    public void Format_UsesTheBrazilianDisplayPattern()
    {
        Assert.Equal("56.087.276/0001-03", CompanyTaxId.Format("56087276000103"));
    }
}
