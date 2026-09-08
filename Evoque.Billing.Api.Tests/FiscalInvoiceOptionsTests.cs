using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;

namespace Evoque.Billing.Api.Tests;

public sealed class FiscalInvoiceOptionsTests
{
    [Fact]
    public void BuildServiceDescription_FormatsTheBillingPeriodWithATrailingPeriod()
    {
        var fiscalInvoiceOptions = new FiscalInvoiceOptions
        {
            MunicipalServiceId = "82367",
            IssTaxRate = 5.00m,
        };

        Assert.Equal(
            "Serviços prestados em 08/2026.",
            fiscalInvoiceOptions.BuildServiceDescription(new BillingPeriodReference(2026, 8)));
    }

    [Fact]
    public void BuildMunicipalServiceName_FormatsTheBillingPeriodWithoutATrailingPeriod()
    {
        var fiscalInvoiceOptions = new FiscalInvoiceOptions
        {
            MunicipalServiceId = "82367",
            IssTaxRate = 5.00m,
        };

        Assert.Equal(
            "Serviços prestados em 08/2026",
            fiscalInvoiceOptions.BuildMunicipalServiceName(new BillingPeriodReference(2026, 8)));
    }

    [Fact]
    public void IsComplete_IsFalseWhenATemplateDoesNotContainTheBillingPeriodPlaceholder()
    {
        var fiscalInvoiceOptions = new FiscalInvoiceOptions
        {
            MunicipalServiceId = "82367",
            IssTaxRate = 5.00m,
            ServiceDescriptionTemplate = "Serviços prestados em {competência}.",
        };

        Assert.False(fiscalInvoiceOptions.IsComplete());
    }

    [Fact]
    public void IsComplete_IsFalseWhenTheIssTaxRateExceedsOneHundredPercent()
    {
        var fiscalInvoiceOptions = new FiscalInvoiceOptions
        {
            MunicipalServiceId = "82367",
            IssTaxRate = 500m,
        };

        Assert.False(fiscalInvoiceOptions.IsComplete());
    }
}
