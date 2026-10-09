using GymShop.Domain.Entities;

namespace GymShop.Application.Abstractions;

public interface IReceiptPdfRenderer
{
    byte[] Render(BillingDocument document);
}

public interface IFiscalInvoicePdfRenderer
{
    byte[] Render(BillingDocument document);
}
