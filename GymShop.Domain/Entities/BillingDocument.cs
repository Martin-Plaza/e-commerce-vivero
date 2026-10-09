using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class BillingDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OrderId { get; set; }
    public int? PaymentId { get; set; }
    public Guid? RelatedDocumentId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public BillingDocumentCategory Category { get; set; } = BillingDocumentCategory.Receipt;
    public BillingDocumentType Type { get; set; } = BillingDocumentType.PurchaseReceipt;
    public BillingDocumentStatus Status { get; set; } = BillingDocumentStatus.Draft;
    public string Currency { get; set; } = "ARS";

    public string IssuerBusinessName { get; set; } = string.Empty;
    public string IssuerCuit { get; set; } = string.Empty;
    public SellerTaxCondition IssuerTaxCondition { get; set; } = SellerTaxCondition.None;
    public string IssuerFiscalAddress { get; set; } = string.Empty;
    public string IssuerGrossIncomeNumber { get; set; } = string.Empty;
    public DateOnly? IssuerActivityStartDate { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public FiscalIdentityDocumentType RecipientDocumentType { get; set; } = FiscalIdentityDocumentType.None;
    public string? RecipientDocumentNumber { get; set; }
    public RecipientTaxCondition RecipientTaxCondition { get; set; } = RecipientTaxCondition.ConsumerFinal;
    public string? RecipientEmail { get; set; }
    public string? RecipientAddress { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal NetTaxedAmount { get; set; }
    public decimal NetUntaxedAmount { get; set; }
    public decimal ExemptAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal OtherTaxesAmount { get; set; }
    public decimal Total { get; set; }

    public int? PointOfSale { get; set; }
    public long? DocumentNumber { get; set; }
    public string? AuthorizationProvider { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? Cae { get; set; }
    public DateOnly? CaeExpiresOn { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public string? RejectionCode { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public Order Order { get; set; } = null!;
    public Payment? Payment { get; set; }
    public BillingDocument? RelatedDocument { get; set; }
    public ICollection<BillingDocument> RelatedDocuments { get; set; } = new List<BillingDocument>();
    public ICollection<BillingDocumentItem> Items { get; set; } = new List<BillingDocumentItem>();
}
