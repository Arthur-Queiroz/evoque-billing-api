using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class FiscalInvoiceTests
{
    private static readonly Guid BillingDraftId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BillingPeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_StartsAsIssuingWithoutAsaasIdentifier()
    {
        var fiscalInvoice = CreateFiscalInvoice();

        Assert.Equal(FiscalInvoiceStatus.Issuing, fiscalInvoice.Status);
        Assert.Null(fiscalInvoice.AsaasInvoiceId);
        Assert.Equal(1, fiscalInvoice.Sequence);
        Assert.Equal(BillingDraftId, fiscalInvoice.BillingDraftId);
        Assert.Equal(BillingPeriodId, fiscalInvoice.BillingPeriodId);
        Assert.Equal("pay_7h844wckdkflengp", fiscalInvoice.AsaasPaymentId);
        Assert.Equal(269.70m, fiscalInvoice.TotalAmount);
        Assert.Equal(new DateOnly(2026, 9, 7), fiscalInvoice.EffectiveDate);
        Assert.False(fiscalInvoice.RetainsIss);
        Assert.Equal("Serviços prestados em 08/2026.", fiscalInvoice.ServiceDescription);
        Assert.Equal(CreatedAt, fiscalInvoice.CreatedAt);
        Assert.Equal(CreatedAt, fiscalInvoice.UpdatedAt);
    }

    [Fact]
    public void Constructor_RefusesAnEmptyBillingDraftId()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            Guid.Empty,
            BillingPeriodId,
            sequence: 1,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt));
    }

    [Fact]
    public void Constructor_RefusesAnEmptyBillingPeriodId()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            BillingDraftId,
            Guid.Empty,
            sequence: 1,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt));
    }

    [Fact]
    public void Constructor_RefusesASequenceLowerThanOne()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            BillingDraftId,
            BillingPeriodId,
            sequence: 0,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt));
    }

    [Fact]
    public void Constructor_RefusesABlankAsaasPaymentId()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            BillingDraftId,
            BillingPeriodId,
            sequence: 1,
            AsaasEnvironment.Production,
            "   ",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt));
    }

    [Fact]
    public void Constructor_RefusesAValueThatIsNotPositive()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            BillingDraftId,
            BillingPeriodId,
            sequence: 1,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            0m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt));
    }

    [Fact]
    public void Constructor_RefusesABlankServiceDescription()
    {
        Assert.Throws<ValidationException>(() => new FiscalInvoice(
            BillingDraftId,
            BillingPeriodId,
            sequence: 1,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "   ",
            CreatedAt));
    }

    [Fact]
    public void MarkScheduled_StoresTheAsaasIdentifier()
    {
        var fiscalInvoice = CreateFiscalInvoice();

        fiscalInvoice.MarkScheduled("inv_000022573933", CreatedAt.AddMinutes(1));

        Assert.Equal(FiscalInvoiceStatus.Scheduled, fiscalInvoice.Status);
        Assert.Equal("inv_000022573933", fiscalInvoice.AsaasInvoiceId);
        Assert.Null(fiscalInvoice.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_KeepsTheRefusalReason()
    {
        var fiscalInvoice = CreateFiscalInvoice();

        fiscalInvoice.MarkFailed("Prefeitura recusou: retenção de ISS obrigatória.", CreatedAt.AddMinutes(1));

        Assert.Equal(FiscalInvoiceStatus.Failed, fiscalInvoice.Status);
        Assert.Equal("Prefeitura recusou: retenção de ISS obrigatória.", fiscalInvoice.ErrorMessage);
        Assert.True(fiscalInvoice.CanBeReissued);
    }

    /// <summary>
    /// Os quatro status abaixo apareceram na leitura real da conta de produção em
    /// 07/09/2026: 171 AUTHORIZED, 13 ERROR, 12 CANCELED e 4 CANCELLATION_DENIED.
    /// </summary>
    [Theory]
    [InlineData("SCHEDULED", FiscalInvoiceStatus.Scheduled)]
    [InlineData("SYNCHRONIZED", FiscalInvoiceStatus.Synchronized)]
    [InlineData("AUTHORIZED", FiscalInvoiceStatus.Authorized)]
    [InlineData("PROCESSING_CANCELLATION", FiscalInvoiceStatus.CancellationRequested)]
    [InlineData("CANCELED", FiscalInvoiceStatus.Canceled)]
    [InlineData("CANCELLATION_DENIED", FiscalInvoiceStatus.CancellationDenied)]
    [InlineData("ERROR", FiscalInvoiceStatus.Failed)]
    public void ApplyAsaasStatus_MapsEveryStatusSeenInProduction(
        string asaasStatus,
        FiscalInvoiceStatus expectedStatus)
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000022573933", CreatedAt.AddMinutes(1));

        fiscalInvoice.ApplyAsaasStatus(asaasStatus, null, CreatedAt.AddMinutes(2));

        Assert.Equal(expectedStatus, fiscalInvoice.Status);
    }

    [Fact]
    public void ApplyAsaasStatus_KeepsThePrefectureReasonWhenTheInvoiceFails()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000022310418", CreatedAt.AddMinutes(1));

        fiscalInvoice.ApplyAsaasStatus(
            "ERROR",
            "Retorno da prefeitura de São Caetano do Sul-SP: esta prestação de serviço deve ter retenção de ISS.",
            CreatedAt.AddMinutes(2));

        Assert.Equal(FiscalInvoiceStatus.Failed, fiscalInvoice.Status);
        Assert.Contains("retenção de ISS", fiscalInvoice.ErrorMessage);
    }

    /// <summary>
    /// Um status novo do Asaas não pode derrubar a sincronização das outras notas.
    /// </summary>
    [Fact]
    public void ApplyAsaasStatus_IgnoresAnUnknownStatus()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000022573933", CreatedAt.AddMinutes(1));

        fiscalInvoice.ApplyAsaasStatus("STATUS_QUE_AINDA_NAO_EXISTE", null, CreatedAt.AddMinutes(2));

        Assert.Equal(FiscalInvoiceStatus.Scheduled, fiscalInvoice.Status);
    }

    [Fact]
    public void MarkScheduled_RefusesAnInvoiceThatIsNotBeingIssued()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000022573933", CreatedAt.AddMinutes(1));

        Assert.Throws<ConflictException>(() =>
            fiscalInvoice.MarkScheduled("inv_000022573999", CreatedAt.AddMinutes(2)));
    }

    /// <summary>
    /// Uma nota já agendada no Asaas só se desfaz por cancelamento; a prefeitura
    /// já negou um cancelamento de competência encerrada, então marcar como
    /// recusada não pode reabrir uma nota que saiu de "Issuing".
    /// </summary>
    [Fact]
    public void MarkFailed_RefusesAnInvoiceThatIsAlreadyScheduled()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000022573933", CreatedAt.AddMinutes(1));

        Assert.Throws<ConflictException>(() =>
            fiscalInvoice.MarkFailed("Falha qualquer.", CreatedAt.AddMinutes(2)));
    }

    private static FiscalInvoice CreateFiscalInvoice()
    {
        return new FiscalInvoice(
            BillingDraftId,
            BillingPeriodId,
            sequence: 1,
            AsaasEnvironment.Production,
            "pay_7h844wckdkflengp",
            269.70m,
            new DateOnly(2026, 9, 7),
            retainsIss: false,
            "Serviços prestados em 08/2026.",
            CreatedAt);
    }
}
