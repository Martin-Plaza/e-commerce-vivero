using GymShop.Domain.Enums;

namespace GymShop.Tests.Domain;

public sealed class BillingDocumentTests
{
    [Fact]
    public void Enum_values_remain_stable_for_persisted_documents()
    {
        Assert.Equal(0, (int)BillingDocumentCategory.Receipt);
        Assert.Equal(1, (int)BillingDocumentCategory.Invoice);
        Assert.Equal(2, (int)BillingDocumentCategory.CreditNote);
        Assert.Equal(0, (int)BillingDocumentStatus.Draft);
        Assert.Equal(1, (int)BillingDocumentStatus.PendingAuthorization);
        Assert.Equal(2, (int)BillingDocumentStatus.Authorized);
        Assert.Equal(3, (int)BillingDocumentStatus.Rejected);
        Assert.Equal(0, (int)BillingDocumentType.PurchaseReceipt);
        Assert.Equal(6, (int)BillingDocumentType.CreditNoteC);
    }
}
