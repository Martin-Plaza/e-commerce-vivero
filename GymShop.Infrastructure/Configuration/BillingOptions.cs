using System.Text.RegularExpressions;
using GymShop.Application.Abstractions;
using GymShop.Domain.Enums;

namespace GymShop.Infrastructure.Configuration;

public sealed class BillingOptions : IBillingProfile
{
    public const string SectionName = "Billing";

    public BillingMode Mode { get; set; } = BillingMode.ReceiptOnly;
    public SellerTaxCondition TaxCondition { get; set; } = SellerTaxCondition.None;
    public string BusinessName { get; set; } = string.Empty;
    public string Cuit { get; set; } = string.Empty;
    public string FiscalAddress { get; set; } = string.Empty;
    public string GrossIncomeNumber { get; set; } = string.Empty;
    public DateOnly? ActivityStartDate { get; set; }
    public int? PointOfSale { get; set; }
    public bool ArcaEnabled { get; set; }

    public bool ElectronicInvoicingReady =>
        Mode == BillingMode.ElectronicInvoice &&
        ArcaEnabled &&
        TaxCondition != SellerTaxCondition.None &&
        !string.IsNullOrWhiteSpace(BusinessName) &&
        IsValidCuit(Cuit) &&
        !string.IsNullOrWhiteSpace(FiscalAddress) &&
        !string.IsNullOrWhiteSpace(GrossIncomeNumber) &&
        ActivityStartDate is not null &&
        PointOfSale is >= 1 and <= 99999;

    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (!string.IsNullOrWhiteSpace(Cuit) && !IsValidCuit(Cuit))
            failures.Add("Billing:Cuit must be a valid Argentine CUIT.");

        if (PointOfSale is < 1 or > 99999)
            failures.Add("Billing:PointOfSale must be between 1 and 99999.");

        if (ActivityStartDate is { } activityStartDate &&
            (activityStartDate.Year < 1900 || activityStartDate > DateOnly.FromDateTime(DateTime.UtcNow)))
            failures.Add("Billing:ActivityStartDate must be a valid date that is not in the future.");

        if (Mode == BillingMode.ElectronicInvoice)
        {
            if (!ArcaEnabled)
                failures.Add("Billing:ArcaEnabled must be true when Billing:Mode is ElectronicInvoice.");
            if (TaxCondition == SellerTaxCondition.None)
                failures.Add("Billing:TaxCondition is required for electronic invoicing.");
            if (string.IsNullOrWhiteSpace(BusinessName))
                failures.Add("Billing:BusinessName is required for electronic invoicing.");
            if (string.IsNullOrWhiteSpace(Cuit))
                failures.Add("Billing:Cuit is required for electronic invoicing.");
            if (string.IsNullOrWhiteSpace(FiscalAddress))
                failures.Add("Billing:FiscalAddress is required for electronic invoicing.");
            if (string.IsNullOrWhiteSpace(GrossIncomeNumber))
                failures.Add("Billing:GrossIncomeNumber is required for electronic invoicing.");
            if (ActivityStartDate is null)
                failures.Add("Billing:ActivityStartDate is required for electronic invoicing.");
            if (PointOfSale is null)
                failures.Add("Billing:PointOfSale is required for electronic invoicing.");
        }

        if (ArcaEnabled && Mode != BillingMode.ElectronicInvoice)
            failures.Add("Billing:Mode must be ElectronicInvoice when ARCA is enabled.");

        return failures;
    }

    internal static bool IsValidCuit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (!Regex.IsMatch(trimmed, @"^(?:\d{11}|\d{2}-\d{8}-\d)$")) return false;

        var digits = Regex.Replace(trimmed, @"\D", string.Empty);
        if (digits.Length != 11 || digits.Distinct().Count() == 1) return false;

        int[] weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var sum = 0;
        for (var index = 0; index < weights.Length; index++)
            sum += (digits[index] - '0') * weights[index];

        var verifier = 11 - sum % 11;
        if (verifier == 11) verifier = 0;
        else if (verifier == 10) verifier = 9;

        return verifier == digits[10] - '0';
    }
}
