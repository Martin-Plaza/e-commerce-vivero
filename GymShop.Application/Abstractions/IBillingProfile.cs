using GymShop.Domain.Enums;

namespace GymShop.Application.Abstractions;

public interface IBillingProfile
{
    BillingMode Mode { get; }
    SellerTaxCondition TaxCondition { get; }
    string BusinessName { get; }
    string Cuit { get; }
    string FiscalAddress { get; }
    string GrossIncomeNumber { get; }
    DateOnly? ActivityStartDate { get; }
    int? PointOfSale { get; }
    bool ArcaEnabled { get; }
    bool ElectronicInvoicingReady { get; }
}
