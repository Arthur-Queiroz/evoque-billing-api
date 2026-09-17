using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargeBatchItemTests
{
    private static readonly Guid BillingDraftId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewItem_HasNoPaymentStatusYet()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        Assert.Equal(ChargePaymentStatus.Unknown, chargeBatchItem.PaymentStatus);
        Assert.Null(chargeBatchItem.PaidAt);
        Assert.False(chargeBatchItem.IsPaymentSettled);
    }

    [Theory]
    [InlineData("PENDING", ChargePaymentStatus.Pending)]
    [InlineData("RECEIVED", ChargePaymentStatus.Received)]
    [InlineData("CONFIRMED", ChargePaymentStatus.Confirmed)]
    [InlineData("OVERDUE", ChargePaymentStatus.Overdue)]
    [InlineData("REFUNDED", ChargePaymentStatus.Refunded)]
    public void ApplyPaymentStatus_MapsWhatTheAccountReturns(
        string asaasStatus,
        ChargePaymentStatus expected)
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus(asaasStatus, null, CreatedAt.AddDays(1));

        Assert.Equal(expected, chargeBatchItem.PaymentStatus);
    }

    /// <summary>
    /// Um status novo do Asaas não pode derrubar a sincronização das demais
    /// cobranças, como já vale para a nota fiscal.
    /// </summary>
    [Fact]
    public void ApplyPaymentStatus_IgnoresAnUnknownStatus()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);
        chargeBatchItem.ApplyPaymentStatus("RECEIVED", new DateOnly(2026, 10, 2), CreatedAt.AddDays(1));

        chargeBatchItem.ApplyPaymentStatus("ALGO_QUE_AINDA_NAO_EXISTE", null, CreatedAt.AddDays(2));

        Assert.Equal(ChargePaymentStatus.Received, chargeBatchItem.PaymentStatus);
    }

    [Fact]
    public void ApplyPaymentStatus_KeepsTheDateItWasPaid()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus("RECEIVED", new DateOnly(2026, 10, 2), CreatedAt.AddDays(1));

        Assert.Equal(new DateOnly(2026, 10, 2), chargeBatchItem.PaidAt);
        Assert.True(chargeBatchItem.IsPaymentSettled);
    }

    /// <summary>
    /// Uma cobrança paga não muda mais, e a sincronização usa isso para não
    /// consultar de novo o que já está resolvido.
    /// </summary>
    [Theory]
    [InlineData("PENDING", false)]
    [InlineData("OVERDUE", false)]
    [InlineData("RECEIVED", true)]
    [InlineData("CONFIRMED", true)]
    [InlineData("REFUNDED", true)]
    public void IsPaymentSettled_IsTrueOnlyForFinalStates(string asaasStatus, bool expected)
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus(asaasStatus, null, CreatedAt.AddDays(1));

        Assert.Equal(expected, chargeBatchItem.IsPaymentSettled);
    }
}
