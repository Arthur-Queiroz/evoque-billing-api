using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class EntryProrationTests
{
    [Fact]
    public void Calculate_CountsTheDaysFromEntryToClosing()
    {
        var result = EntryProration.Calculate(90m, new DateOnly(2026, 6, 14), new DateOnly(2026, 6, 25));

        Assert.Equal(EntryProrationKind.Prorated, result.Kind);
        Assert.Equal(11, result.BilledDays);
        Assert.Equal(33m, result.Amount);
    }

    [Fact]
    public void Calculate_RoundsToCents()
    {
        var result = EntryProration.Calculate(89.90m, new DateOnly(2026, 6, 14), new DateOnly(2026, 6, 25));

        Assert.Equal(32.96m, result.Amount);
    }

    [Fact]
    public void Calculate_AlwaysUsesThirtyDaysAsTheBase()
    {
        // Período de 31 dias (26/07 a 25/08): os mesmos 11 dias valem o mesmo.
        var result = EntryProration.Calculate(90m, new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 25));

        Assert.Equal(33m, result.Amount);
    }

    [Theory]
    [InlineData(2026, 5, 26, 2026, 6, 25)]
    [InlineData(2026, 7, 26, 2026, 8, 25)]
    [InlineData(2026, 2, 26, 2026, 3, 25)]
    [InlineData(2026, 1, 3, 2026, 2, 2)]
    public void Calculate_ChargesTheFullValueToWhoJoinedOnTheFirstDayOfThePeriod(
        int entryYear, int entryMonth, int entryDay, int closingYear, int closingMonth, int closingDay)
    {
        var result = EntryProration.Calculate(
            89.90m,
            new DateOnly(entryYear, entryMonth, entryDay),
            new DateOnly(closingYear, closingMonth, closingDay));

        Assert.Equal(EntryProrationKind.FullPeriod, result.Kind);
        Assert.Equal(89.90m, result.Amount);
    }

    [Fact]
    public void Calculate_ChargesTheFullValueToWhoJoinedBeforeThePeriod()
    {
        var result = EntryProration.Calculate(89.90m, new DateOnly(2024, 4, 22), new DateOnly(2026, 6, 25));

        Assert.Equal(EntryProrationKind.FullPeriod, result.Kind);
        Assert.Equal(89.90m, result.Amount);
    }

    [Fact]
    public void Calculate_NeverChargesMoreThanTheFullValue()
    {
        // 27/07 com fechamento em 25/08: 29 dias, ainda proporcional.
        var almostFull = EntryProration.Calculate(90m, new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 25));

        Assert.Equal(29, almostFull.BilledDays);
        Assert.Equal(87m, almostFull.Amount);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(26)]
    public void Calculate_LeavesOutWhoJoinedOnOrAfterTheClosing(int entryDay)
    {
        var result = EntryProration.Calculate(89.90m, new DateOnly(2026, 6, entryDay), new DateOnly(2026, 6, 25));

        Assert.Equal(EntryProrationKind.OutsidePeriod, result.Kind);
        Assert.Equal(0m, result.Amount);
    }
}
