namespace GymShop.Application.DTOs.Billing;

public sealed record BillingDocumentItemResponse(
    int Id,
    int? OrderItemId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalAmount);

public sealed record BillingDocumentResponse(
    Guid Id,
    int OrderId,
    int? PaymentId,
    Guid? RelatedDocumentId,
    string Category,
    string Type,
    string Status,
    string Currency,
    string IssuerBusinessName,
    string RecipientName,
    string? RecipientEmail,
    string? RecipientAddress,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal Total,
    int? PointOfSale,
    long? DocumentNumber,
    string? AuthorizationProvider,
    string? Cae,
    DateOnly? CaeExpiresOn,
    string? RejectionCode,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? AuthorizedAtUtc,
    IReadOnlyList<BillingDocumentItemResponse> Items);

public sealed record BillingDocumentPdfResponse(
    byte[] Content,
    string FileName,
    string ContentType = "application/pdf");
