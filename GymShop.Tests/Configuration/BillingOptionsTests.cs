using GymShop.Domain.Enums;
using GymShop.Infrastructure.Configuration;

namespace GymShop.Tests.Configuration;

public sealed class BillingOptionsTests
{
    [Fact]
    public void Receipt_only_is_valid_without_fiscal_registration()
    {
        var options = new BillingOptions();

        Assert.Empty(options.Validate());
        Assert.False(options.ElectronicInvoicingReady);
    }

    [Fact]
    public void Electronic_invoice_requires_complete_fiscal_profile()
    {
        var options = new BillingOptions
        {
            Mode = BillingMode.ElectronicInvoice,
            ArcaEnabled = true
        };

        var failures = options.Validate();

        Assert.Contains(failures, value => value.Contains("TaxCondition", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("BusinessName", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("Cuit", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("FiscalAddress", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("GrossIncomeNumber", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("ActivityStartDate", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("PointOfSale", StringComparison.Ordinal));
        Assert.False(options.ElectronicInvoicingReady);
    }

    [Theory]
    [InlineData(SellerTaxCondition.Monotributo)]
    [InlineData(SellerTaxCondition.RegisteredTaxpayer)]
    [InlineData(SellerTaxCondition.Exempt)]
    public void Supported_tax_conditions_can_enable_electronic_invoicing(SellerTaxCondition taxCondition)
    {
        var options = new BillingOptions
        {
            Mode = BillingMode.ElectronicInvoice,
            TaxCondition = taxCondition,
            BusinessName = "Comercio de prueba",
            Cuit = "30-53625919-4",
            FiscalAddress = "Catamarca 2730, Rosario, Santa Fe",
            GrossIncomeNumber = "Exento",
            ActivityStartDate = new DateOnly(2020, 1, 1),
            PointOfSale = 1,
            ArcaEnabled = true
        };

        Assert.Empty(options.Validate());
        Assert.True(options.ElectronicInvoicingReady);
    }

    [Theory]
    [InlineData("30-53625919-4")]
    [InlineData("30536259194")]
    public void Cuit_accepts_formatted_or_unformatted_valid_values(string cuit)
    {
        var options = new BillingOptions { Cuit = cuit };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Invalid_optional_cuit_is_rejected_early()
    {
        var options = new BillingOptions { Cuit = "30-00000000-0" };

        Assert.Contains(options.Validate(), value => value.Contains("valid Argentine CUIT", StringComparison.Ordinal));
    }

    [Fact]
    public void Arca_cannot_be_enabled_in_receipt_only_mode()
    {
        var options = new BillingOptions { ArcaEnabled = true };

        Assert.Contains(options.Validate(), value => value.Contains("Mode must be ElectronicInvoice", StringComparison.Ordinal));
    }
}
