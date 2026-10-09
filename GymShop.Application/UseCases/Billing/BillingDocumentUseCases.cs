using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Billing;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Billing;

public interface ICreateOrderReceiptUseCase
{
    Task<AppResult<BillingDocumentResponse>> ExecuteAsync(int orderId, string? idempotencyKey, CancellationToken cancellationToken = default);
}

public interface IGetOrderBillingDocumentsUseCase
{
    Task<AppResult<List<BillingDocumentResponse>>> ExecuteAsync(int orderId, CancellationToken cancellationToken = default);
}

public interface IGetBillingDocumentPdfUseCase
{
    Task<AppResult<BillingDocumentPdfResponse>> ExecuteAsync(int orderId, Guid documentId, CancellationToken cancellationToken = default);
}

public interface IGetCustomerOrderBillingDocumentsUseCase
{
    Task<AppResult<List<BillingDocumentResponse>>> ExecuteAsync(int orderId, int userId, CancellationToken cancellationToken = default);
}

public interface IGetCustomerBillingDocumentPdfUseCase
{
    Task<AppResult<BillingDocumentPdfResponse>> ExecuteAsync(int orderId, Guid documentId, int userId, CancellationToken cancellationToken = default);
}

public interface IGetArcaConnectionStatusUseCase
{
    Task<AppResult<ArcaConnectionStatus>> ExecuteAsync(CancellationToken cancellationToken = default);
}

public interface ICreateArcaHomologationInvoiceUseCase
{
    Task<AppResult<BillingDocumentResponse>> ExecuteAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ICreateArcaHomologationCreditNoteUseCase
{
    Task<AppResult<BillingDocumentResponse>> ExecuteAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class CreateOrderReceiptUseCase(
    IApplicationDbContext db,
    IBillingProfile billingProfile,
    TimeProvider timeProvider,
    IAuditContext? auditContext = null) : ICreateOrderReceiptUseCase
{
    public async Task<AppResult<BillingDocumentResponse>> ExecuteAsync(int orderId, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > ValidationLimits.IdempotencyKey)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia del comprobante no es valida.");

        var documentForKey = await db.BillingDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);

        if (documentForKey is not null)
        {
            if (documentForKey.OrderId != orderId || documentForKey.Category != BillingDocumentCategory.Receipt)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada para otro comprobante.");
            return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(documentForKey));
        }

        var existing = await db.BillingDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.OrderId == orderId && x.Category == BillingDocumentCategory.Receipt, cancellationToken);
        if (existing is not null)
            return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(existing));

        if (billingProfile.Mode != BillingMode.ReceiptOnly)
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Conflict,
                "La emision fiscal todavia no esta habilitada: falta integrar la autorizacion electronica de ARCA.",
                "electronic_invoicing_not_implemented");

        var order = await db.Orders
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");

        if (order.Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered))
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "Solo se puede generar un comprobante para un pedido pagado.");

        var approvedPayment = order.Payments
            .Where(x => x.Status == PaymentStatus.Approved)
            .OrderByDescending(x => x.PaidAt ?? x.UpdatedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (order.Total > 0 && approvedPayment is null)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "El pedido no tiene un pago aprobado para respaldar el comprobante.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var document = new BillingDocument
        {
            OrderId = order.Id,
            PaymentId = approvedPayment?.Id,
            IdempotencyKey = key,
            Category = BillingDocumentCategory.Receipt,
            Type = BillingDocumentType.PurchaseReceipt,
            Status = BillingDocumentStatus.Authorized,
            Currency = approvedPayment?.Currency ?? "ARS",
            IssuerBusinessName = billingProfile.BusinessName.Trim(),
            IssuerCuit = billingProfile.Cuit.Trim(),
            IssuerTaxCondition = billingProfile.TaxCondition,
            IssuerFiscalAddress = billingProfile.FiscalAddress.Trim(),
            IssuerGrossIncomeNumber = billingProfile.GrossIncomeNumber.Trim(),
            IssuerActivityStartDate = billingProfile.ActivityStartDate,
            RecipientName = order.User is null
                ? $"{order.GuestFirstName} {order.GuestLastName}".Trim()
                : $"{order.User.Name} {order.User.LastName}".Trim(),
            RecipientDocumentType = FiscalIdentityDocumentType.None,
            RecipientTaxCondition = RecipientTaxCondition.ConsumerFinal,
            RecipientEmail = order.User?.Email ?? order.GuestEmail ?? string.Empty,
            RecipientAddress = order.ShippingAddress,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            ShippingAmount = order.ShippingCost,
            Total = order.Total,
            AuthorizationProvider = "Internal",
            AuthorizedAtUtc = now,
            CreatedAtUtc = now
        };

        foreach (var item in order.Items.OrderBy(x => x.Id))
        {
            document.Items.Add(new BillingDocumentItem
            {
                OrderItemId = item.Id,
                Description = string.IsNullOrWhiteSpace(item.VariantSku) ? item.ProductName : $"{item.ProductName} ({item.VariantSku})",
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NetAmount = item.Subtotal,
                TotalAmount = item.Subtotal
            });
        }

        db.BillingDocuments.Add(document);
        AuditTrail.Add(db, auditContext, "PurchaseReceiptCreated", "Order", order.Id, null,
            new { documentId = document.Id, status = document.Status.ToString(), type = document.Type.ToString(), total = document.Total },
            "Comprobante interno generado manualmente.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.BillingDocuments.Remove(document);
            var winner = await db.BillingDocuments
                .AsNoTracking()
                .Include(x => x.Items)
                .Where(x => x.IdempotencyKey == key ||
                            (x.OrderId == orderId && x.Category == BillingDocumentCategory.Receipt))
                .OrderByDescending(x => x.IdempotencyKey == key)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (winner is null) throw;
            if (winner.IdempotencyKey == key && winner.OrderId != orderId)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada para otro comprobante.");
            return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(winner));
        }

        return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(document));
    }
}

public sealed class GetOrderBillingDocumentsUseCase(IApplicationDbContext db) : IGetOrderBillingDocumentsUseCase
{
    public async Task<AppResult<List<BillingDocumentResponse>>> ExecuteAsync(int orderId, CancellationToken cancellationToken = default)
    {
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId, cancellationToken))
            return AppResult<List<BillingDocumentResponse>>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");

        var documents = await db.BillingDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.OrderId == orderId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return AppResult<List<BillingDocumentResponse>>.Success(documents.Select(BillingDocumentMapper.ToResponse).ToList());
    }
}

public sealed class GetCustomerOrderBillingDocumentsUseCase(IApplicationDbContext db) : IGetCustomerOrderBillingDocumentsUseCase
{
    public async Task<AppResult<List<BillingDocumentResponse>>> ExecuteAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.UserId == userId, cancellationToken))
            return AppResult<List<BillingDocumentResponse>>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");

        var documents = await db.BillingDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.OrderId == orderId && x.Status == BillingDocumentStatus.Authorized)
            .OrderByDescending(x => x.AuthorizedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return AppResult<List<BillingDocumentResponse>>.Success(documents.Select(BillingDocumentMapper.ToResponse).ToList());
    }
}

public sealed class GetBillingDocumentPdfUseCase(
    IApplicationDbContext db,
    IReceiptPdfRenderer receiptRenderer,
    IFiscalInvoicePdfRenderer fiscalInvoiceRenderer) : IGetBillingDocumentPdfUseCase
{
    public async Task<AppResult<BillingDocumentPdfResponse>> ExecuteAsync(
        int orderId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await db.BillingDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.RelatedDocument)
            .SingleOrDefaultAsync(x => x.Id == documentId && x.OrderId == orderId, cancellationToken);

        if (document is null)
            return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.NotFound, "Comprobante no encontrado.");
        if (document.Status != BillingDocumentStatus.Authorized)
            return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.Conflict, "El comprobante todavia no esta autorizado.");

        if (document.Category == BillingDocumentCategory.Receipt && document.Type == BillingDocumentType.PurchaseReceipt)
            return AppResult<BillingDocumentPdfResponse>.Success(new BillingDocumentPdfResponse(
                receiptRenderer.Render(document),
                $"pedido-{orderId}-comprobante-{document.Id:N}.pdf"));

        if (document.Category == BillingDocumentCategory.Invoice &&
            document.Type is BillingDocumentType.InvoiceA or BillingDocumentType.InvoiceB or BillingDocumentType.InvoiceC)
        {
            if (document.PointOfSale is null || document.DocumentNumber is null ||
                string.IsNullOrWhiteSpace(document.Cae) || document.CaeExpiresOn is null)
                return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.Conflict, "La factura autorizada no tiene datos fiscales completos.");
            return AppResult<BillingDocumentPdfResponse>.Success(new BillingDocumentPdfResponse(
                fiscalInvoiceRenderer.Render(document),
                $"pedido-{orderId}-factura-{document.PointOfSale:00000}-{document.DocumentNumber:00000000}.pdf"));
        }

        if (document.Category == BillingDocumentCategory.CreditNote &&
            document.Type is BillingDocumentType.CreditNoteA or BillingDocumentType.CreditNoteB or BillingDocumentType.CreditNoteC)
        {
            if (document.PointOfSale is null || document.DocumentNumber is null ||
                string.IsNullOrWhiteSpace(document.Cae) || document.CaeExpiresOn is null || document.RelatedDocument is null)
                return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.Conflict, "La nota de credito autorizada no tiene datos fiscales completos.");
            return AppResult<BillingDocumentPdfResponse>.Success(new BillingDocumentPdfResponse(
                fiscalInvoiceRenderer.Render(document),
                $"pedido-{orderId}-nota-credito-{document.PointOfSale:00000}-{document.DocumentNumber:00000000}.pdf"));
        }

        return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.Conflict, "El tipo de documento no tiene una representacion PDF disponible.");
    }
}

public sealed class GetCustomerBillingDocumentPdfUseCase(
    IApplicationDbContext db,
    IGetBillingDocumentPdfUseCase getBillingDocumentPdf) : IGetCustomerBillingDocumentPdfUseCase
{
    public async Task<AppResult<BillingDocumentPdfResponse>> ExecuteAsync(
        int orderId,
        Guid documentId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.UserId == userId, cancellationToken))
            return AppResult<BillingDocumentPdfResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        return await getBillingDocumentPdf.ExecuteAsync(orderId, documentId, cancellationToken);
    }
}

public sealed class CreateArcaHomologationInvoiceUseCase(
    IApplicationDbContext db,
    IBillingProfile billingProfile,
    IArcaElectronicInvoiceGateway gateway,
    TimeProvider timeProvider,
    IStoreTimeZone storeTimeZone,
    IAuditContext? auditContext = null) : ICreateArcaHomologationInvoiceUseCase
{
    private static readonly SemaphoreSlim IssuanceLock = new(1, 1);

    public async Task<AppResult<BillingDocumentResponse>> ExecuteAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > ValidationLimits.IdempotencyKey)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia de la factura no es valida.");

        await IssuanceLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await db.BillingDocuments
                .Include(x => x.Items)
                .Where(x => x.IdempotencyKey == key ||
                            (x.OrderId == orderId && x.Category == BillingDocumentCategory.Invoice))
                .OrderByDescending(x => x.IdempotencyKey == key)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                if (existing.OrderId != orderId || existing.Category != BillingDocumentCategory.Invoice)
                    return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada para otro comprobante.");
                if (existing.Status == BillingDocumentStatus.Authorized)
                    return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(existing));
                var reconcileFirst = existing.Status == BillingDocumentStatus.PendingAuthorization;
                if (existing.Status == BillingDocumentStatus.Rejected)
                {
                    existing.Status = BillingDocumentStatus.PendingAuthorization;
                    existing.RejectionCode = null;
                    existing.RejectionReason = null;
                    existing.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                    await db.SaveChangesAsync(cancellationToken);
                }
                return await AuthorizeAsync(existing, reconcileFirst, cancellationToken);
            }

            var order = await db.Orders
                .Include(x => x.User)
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (order is null)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
            if (order.Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered))
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "Solo se puede emitir una factura de prueba para un pedido pagado.");
            if (order.Total <= 0)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "ARCA no admite una factura con total cero.");

            var approvedPayment = order.Payments
                .Where(x => x.Status == PaymentStatus.Approved)
                .OrderByDescending(x => x.PaidAt ?? x.UpdatedAt ?? x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            if (approvedPayment is null)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "El pedido no tiene un pago aprobado para respaldar la factura.");

            ArcaInvoiceSequence sequence;
            try
            {
                sequence = await gateway.GetNextHomologationInvoiceSequenceAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Unavailable, "No se pudo consultar la numeracion de ARCA.", "arca_unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Unavailable, "ARCA no respondio a tiempo al consultar la numeracion.", "arca_timeout");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var document = new BillingDocument
            {
                OrderId = order.Id,
                PaymentId = approvedPayment.Id,
                IdempotencyKey = key,
                Category = BillingDocumentCategory.Invoice,
                Type = BillingDocumentType.InvoiceC,
                Status = BillingDocumentStatus.PendingAuthorization,
                Currency = "ARS",
                IssuerBusinessName = string.IsNullOrWhiteSpace(billingProfile.BusinessName)
                    ? "GymShop Homologacion"
                    : billingProfile.BusinessName.Trim(),
                IssuerCuit = billingProfile.Cuit.Trim(),
                IssuerTaxCondition = billingProfile.TaxCondition,
                IssuerFiscalAddress = billingProfile.FiscalAddress.Trim(),
                IssuerGrossIncomeNumber = billingProfile.GrossIncomeNumber.Trim(),
                IssuerActivityStartDate = billingProfile.ActivityStartDate,
                RecipientName = order.User is null
                    ? $"{order.GuestFirstName} {order.GuestLastName}".Trim()
                    : $"{order.User.Name} {order.User.LastName}".Trim(),
                RecipientDocumentType = FiscalIdentityDocumentType.None,
                RecipientTaxCondition = RecipientTaxCondition.ConsumerFinal,
                RecipientEmail = order.User?.Email ?? order.GuestEmail ?? string.Empty,
                RecipientAddress = order.ShippingAddress,
                Subtotal = order.Subtotal,
                DiscountAmount = order.DiscountAmount,
                ShippingAmount = order.ShippingCost,
                NetTaxedAmount = order.Total,
                Total = order.Total,
                PointOfSale = sequence.PointOfSale,
                DocumentNumber = sequence.DocumentNumber,
                AuthorizationProvider = "ARCA-Homologation",
                ProviderRequestId = $"{sequence.PointOfSale}-{sequence.InvoiceType}-{sequence.DocumentNumber}",
                CreatedAtUtc = now
            };
            foreach (var item in order.Items.OrderBy(x => x.Id))
            {
                document.Items.Add(new BillingDocumentItem
                {
                    OrderItemId = item.Id,
                    Description = string.IsNullOrWhiteSpace(item.VariantSku) ? item.ProductName : $"{item.ProductName} ({item.VariantSku})",
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    NetAmount = item.Subtotal,
                    TotalAmount = item.Subtotal
                });
            }

            db.BillingDocuments.Add(document);
            AuditTrail.Add(db, auditContext, "ArcaHomologationInvoiceRequested", "Order", order.Id, null,
                new { documentId = document.Id, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber, total = document.Total },
                "Factura C de homologacion enviada manualmente a ARCA.");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.BillingDocuments.Remove(document);
                var winner = await db.BillingDocuments.AsNoTracking().Include(x => x.Items)
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == key ||
                                              (x.OrderId == orderId && x.Category == BillingDocumentCategory.Invoice), cancellationToken);
                if (winner is null) throw;
                return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(winner));
            }

            return await AuthorizeAsync(document, false, cancellationToken);
        }
        finally
        {
            IssuanceLock.Release();
        }
    }

    private async Task<AppResult<BillingDocumentResponse>> AuthorizeAsync(
        BillingDocument document,
        bool reconcileFirst,
        CancellationToken cancellationToken)
    {
        if (document.PointOfSale is null || document.DocumentNumber is null)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La factura pendiente no tiene numeracion fiscal.");

        var issuedAt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(document.CreatedAtUtc, DateTimeKind.Utc),
            storeTimeZone.TimeZone);
        if (reconcileFirst)
        {
            AppResult<BillingDocumentResponse>? recovered;
            try
            {
                recovered = await RecoverAuthorizedDocumentAsync(document, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return AppResult<BillingDocumentResponse>.Failure(
                    AppErrorType.Unavailable,
                    "No se pudo confirmar si ARCA ya autorizo la factura. No se reenvio para evitar duplicar la solicitud.",
                    "arca_reconciliation_unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AppResult<BillingDocumentResponse>.Failure(
                    AppErrorType.Unavailable,
                    "ARCA no respondio al consultar la factura pendiente. No se reenvio para evitar duplicar la solicitud.",
                    "arca_reconciliation_timeout");
            }
            if (recovered is not null) return recovered;
        }
        ArcaInvoiceAuthorization authorization;
        try
        {
            authorization = await gateway.AuthorizeHomologationInvoiceAsync(new ArcaInvoiceAuthorizationRequest(
                document.PointOfSale.Value,
                11,
                document.DocumentNumber.Value,
                DateOnly.FromDateTime(issuedAt),
                document.Total), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Unavailable,
                "No se obtuvo respuesta de ARCA. La factura quedo pendiente y puede reintentarse con la misma clave.",
                "arca_unavailable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Unavailable,
                "ARCA no respondio a tiempo. La factura quedo pendiente y puede reintentarse con la misma clave.",
                "arca_timeout");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        document.UpdatedAtUtc = now;
        if (authorization.Authorized)
        {
            document.Status = BillingDocumentStatus.Authorized;
            document.Cae = authorization.Cae;
            document.CaeExpiresOn = authorization.CaeExpiresOn;
            document.AuthorizedAtUtc = now;
            document.RejectionCode = null;
            document.RejectionReason = null;
            AuditTrail.Add(db, auditContext, "ArcaHomologationInvoiceAuthorized", "Order", document.OrderId, null,
                new { documentId = document.Id, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber },
                "ARCA autorizo la factura C de homologacion.");
        }
        else
        {
            document.Status = BillingDocumentStatus.Rejected;
            document.RejectionCode = authorization.RejectionCode;
            document.RejectionReason = authorization.RejectionReason;
            AuditTrail.Add(db, auditContext, "ArcaHomologationInvoiceRejected", "Order", document.OrderId, null,
                new { documentId = document.Id, code = document.RejectionCode },
                "ARCA rechazo la factura C de homologacion.");
        }
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(document));
    }

    private async Task<AppResult<BillingDocumentResponse>?> RecoverAuthorizedDocumentAsync(
        BillingDocument document,
        CancellationToken cancellationToken)
    {
        var recovered = await gateway.GetAuthorizedHomologationDocumentAsync(
            document.PointOfSale!.Value, 11, document.DocumentNumber!.Value, cancellationToken);
        if (recovered is null) return null;
        if (recovered.Total != document.Total)
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Conflict,
                "ARCA devolvio una factura con un importe distinto al documento pendiente.",
                "arca_reconciliation_mismatch");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        document.Status = BillingDocumentStatus.Authorized;
        document.Cae = recovered.Cae;
        document.CaeExpiresOn = recovered.CaeExpiresOn;
        document.AuthorizedAtUtc = now;
        document.UpdatedAtUtc = now;
        document.RejectionCode = null;
        document.RejectionReason = null;
        AuditTrail.Add(db, auditContext, "ArcaHomologationInvoiceRecovered", "Order", document.OrderId, null,
            new { documentId = document.Id, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber },
            "La factura pendiente fue recuperada mediante FECompConsultar sin reenviar la solicitud.");
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(document));
    }
}

public sealed class CreateArcaHomologationCreditNoteUseCase(
    IApplicationDbContext db,
    IArcaElectronicInvoiceGateway gateway,
    TimeProvider timeProvider,
    IStoreTimeZone storeTimeZone,
    IAuditContext? auditContext = null) : ICreateArcaHomologationCreditNoteUseCase
{
    private static readonly SemaphoreSlim IssuanceLock = new(1, 1);

    public async Task<AppResult<BillingDocumentResponse>> ExecuteAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > ValidationLimits.IdempotencyKey)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia de la nota de credito no es valida.");

        await IssuanceLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await db.BillingDocuments
                .Include(x => x.Items)
                .Include(x => x.RelatedDocument)
                .Where(x => x.IdempotencyKey == key ||
                            (x.OrderId == orderId && x.Category == BillingDocumentCategory.CreditNote))
                .OrderByDescending(x => x.IdempotencyKey == key)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                if (existing.OrderId != orderId || existing.Category != BillingDocumentCategory.CreditNote)
                    return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada para otro comprobante.");
                if (existing.Status == BillingDocumentStatus.Authorized)
                    return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(existing));
                var reconcileFirst = existing.Status == BillingDocumentStatus.PendingAuthorization;
                if (existing.Status == BillingDocumentStatus.Rejected)
                {
                    existing.Status = BillingDocumentStatus.PendingAuthorization;
                    existing.RejectionCode = null;
                    existing.RejectionReason = null;
                    existing.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                    await db.SaveChangesAsync(cancellationToken);
                }
                return await AuthorizeAsync(existing, reconcileFirst, cancellationToken);
            }

            var order = await db.Orders
                .Include(x => x.Payments)
                .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (order is null)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
            if (order.Status != OrderStatus.Refunded || !order.Payments.Any(x => x.Status == PaymentStatus.Refunded))
                return AppResult<BillingDocumentResponse>.Failure(
                    AppErrorType.Conflict,
                    "La nota de credito solo puede emitirse despues de que el proveedor confirme el reembolso total.");

            var invoice = await db.BillingDocuments
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.OrderId == orderId &&
                    x.Category == BillingDocumentCategory.Invoice, cancellationToken);
            if (invoice is null)
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "El pedido no tiene una factura para anular.");
            if (invoice.Type != BillingDocumentType.InvoiceC || invoice.Status != BillingDocumentStatus.Authorized ||
                invoice.PointOfSale is null || invoice.DocumentNumber is null || string.IsNullOrWhiteSpace(invoice.Cae))
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La factura C asociada no esta autorizada o tiene datos fiscales incompletos.");
            if (!string.Equals(invoice.AuthorizationProvider, "ARCA-Homologation", StringComparison.Ordinal))
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "Solo se pueden anular facturas emitidas en homologacion desde este flujo.");

            ArcaInvoiceSequence sequence;
            try
            {
                sequence = await gateway.GetNextHomologationCreditNoteSequenceAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Unavailable, "No se pudo consultar la numeracion de ARCA.", "arca_unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Unavailable, "ARCA no respondio a tiempo al consultar la numeracion.", "arca_timeout");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var document = new BillingDocument
            {
                OrderId = order.Id,
                PaymentId = invoice.PaymentId,
                RelatedDocumentId = invoice.Id,
                RelatedDocument = invoice,
                IdempotencyKey = key,
                Category = BillingDocumentCategory.CreditNote,
                Type = BillingDocumentType.CreditNoteC,
                Status = BillingDocumentStatus.PendingAuthorization,
                Currency = invoice.Currency,
                IssuerBusinessName = invoice.IssuerBusinessName,
                IssuerCuit = invoice.IssuerCuit,
                IssuerTaxCondition = invoice.IssuerTaxCondition,
                IssuerFiscalAddress = invoice.IssuerFiscalAddress,
                IssuerGrossIncomeNumber = invoice.IssuerGrossIncomeNumber,
                IssuerActivityStartDate = invoice.IssuerActivityStartDate,
                RecipientName = invoice.RecipientName,
                RecipientDocumentType = invoice.RecipientDocumentType,
                RecipientDocumentNumber = invoice.RecipientDocumentNumber,
                RecipientTaxCondition = invoice.RecipientTaxCondition,
                RecipientEmail = invoice.RecipientEmail,
                RecipientAddress = invoice.RecipientAddress,
                Subtotal = invoice.Subtotal,
                DiscountAmount = invoice.DiscountAmount,
                ShippingAmount = invoice.ShippingAmount,
                NetTaxedAmount = invoice.NetTaxedAmount,
                NetUntaxedAmount = invoice.NetUntaxedAmount,
                ExemptAmount = invoice.ExemptAmount,
                VatAmount = invoice.VatAmount,
                OtherTaxesAmount = invoice.OtherTaxesAmount,
                Total = invoice.Total,
                PointOfSale = sequence.PointOfSale,
                DocumentNumber = sequence.DocumentNumber,
                AuthorizationProvider = "ARCA-Homologation",
                ProviderRequestId = $"{sequence.PointOfSale}-{sequence.InvoiceType}-{sequence.DocumentNumber}",
                CreatedAtUtc = now
            };
            foreach (var item in invoice.Items.OrderBy(x => x.Id))
            {
                document.Items.Add(new BillingDocumentItem
                {
                    OrderItemId = item.OrderItemId,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    NetAmount = item.NetAmount,
                    VatRate = item.VatRate,
                    VatAmount = item.VatAmount,
                    TotalAmount = item.TotalAmount
                });
            }

            db.BillingDocuments.Add(document);
            AuditTrail.Add(db, auditContext, "ArcaHomologationCreditNoteRequested", "Order", order.Id, null,
                new { documentId = document.Id, relatedDocumentId = invoice.Id, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber, total = document.Total },
                "Nota de credito C de homologacion enviada manualmente a ARCA.");
            await db.SaveChangesAsync(cancellationToken);
            return await AuthorizeAsync(document, false, cancellationToken);
        }
        finally
        {
            IssuanceLock.Release();
        }
    }

    private async Task<AppResult<BillingDocumentResponse>> AuthorizeAsync(
        BillingDocument document,
        bool reconcileFirst,
        CancellationToken cancellationToken)
    {
        var invoice = document.RelatedDocument;
        if (document.PointOfSale is null || document.DocumentNumber is null || invoice is null ||
            invoice.PointOfSale is null || invoice.DocumentNumber is null)
            return AppResult<BillingDocumentResponse>.Failure(AppErrorType.Conflict, "La nota de credito pendiente no tiene numeracion o factura asociada.");

        var issuedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(document.CreatedAtUtc, DateTimeKind.Utc), storeTimeZone.TimeZone);
        if (reconcileFirst)
        {
            AppResult<BillingDocumentResponse>? recovered;
            try
            {
                recovered = await RecoverAuthorizedDocumentAsync(document, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return AppResult<BillingDocumentResponse>.Failure(
                    AppErrorType.Unavailable,
                    "No se pudo confirmar si ARCA ya autorizo la nota de credito. No se reenvio para evitar duplicar la solicitud.",
                    "arca_reconciliation_unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AppResult<BillingDocumentResponse>.Failure(
                    AppErrorType.Unavailable,
                    "ARCA no respondio al consultar la nota de credito pendiente. No se reenvio para evitar duplicar la solicitud.",
                    "arca_reconciliation_timeout");
            }
            if (recovered is not null) return recovered;
        }
        var invoiceIssuedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(invoice.CreatedAtUtc, DateTimeKind.Utc), storeTimeZone.TimeZone);
        ArcaInvoiceAuthorization authorization;
        try
        {
            authorization = await gateway.AuthorizeHomologationCreditNoteAsync(new ArcaCreditNoteAuthorizationRequest(
                document.PointOfSale.Value,
                13,
                document.DocumentNumber.Value,
                DateOnly.FromDateTime(issuedAt),
                document.Total,
                11,
                invoice.PointOfSale.Value,
                invoice.DocumentNumber.Value,
                DateOnly.FromDateTime(invoiceIssuedAt)), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Unavailable,
                "No se obtuvo respuesta de ARCA. La nota de credito quedo pendiente y puede reintentarse con la misma clave.",
                "arca_unavailable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Unavailable,
                "ARCA no respondio a tiempo. La nota de credito quedo pendiente y puede reintentarse con la misma clave.",
                "arca_timeout");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        document.UpdatedAtUtc = now;
        if (authorization.Authorized)
        {
            document.Status = BillingDocumentStatus.Authorized;
            document.Cae = authorization.Cae;
            document.CaeExpiresOn = authorization.CaeExpiresOn;
            document.AuthorizedAtUtc = now;
            document.RejectionCode = null;
            document.RejectionReason = null;
            AuditTrail.Add(db, auditContext, "ArcaHomologationCreditNoteAuthorized", "Order", document.OrderId, null,
                new { documentId = document.Id, relatedDocumentId = document.RelatedDocumentId, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber },
                "ARCA autorizo la nota de credito C de homologacion.");
        }
        else
        {
            document.Status = BillingDocumentStatus.Rejected;
            document.RejectionCode = authorization.RejectionCode;
            document.RejectionReason = authorization.RejectionReason;
            AuditTrail.Add(db, auditContext, "ArcaHomologationCreditNoteRejected", "Order", document.OrderId, null,
                new { documentId = document.Id, relatedDocumentId = document.RelatedDocumentId, code = document.RejectionCode },
                "ARCA rechazo la nota de credito C de homologacion.");
        }
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(document));
    }

    private async Task<AppResult<BillingDocumentResponse>?> RecoverAuthorizedDocumentAsync(
        BillingDocument document,
        CancellationToken cancellationToken)
    {
        var recovered = await gateway.GetAuthorizedHomologationDocumentAsync(
            document.PointOfSale!.Value, 13, document.DocumentNumber!.Value, cancellationToken);
        if (recovered is null) return null;
        if (recovered.Total != document.Total)
            return AppResult<BillingDocumentResponse>.Failure(
                AppErrorType.Conflict,
                "ARCA devolvio una nota de credito con un importe distinto al documento pendiente.",
                "arca_reconciliation_mismatch");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        document.Status = BillingDocumentStatus.Authorized;
        document.Cae = recovered.Cae;
        document.CaeExpiresOn = recovered.CaeExpiresOn;
        document.AuthorizedAtUtc = now;
        document.UpdatedAtUtc = now;
        document.RejectionCode = null;
        document.RejectionReason = null;
        AuditTrail.Add(db, auditContext, "ArcaHomologationCreditNoteRecovered", "Order", document.OrderId, null,
            new { documentId = document.Id, relatedDocumentId = document.RelatedDocumentId, pointOfSale = document.PointOfSale, documentNumber = document.DocumentNumber },
            "La nota de credito pendiente fue recuperada mediante FECompConsultar sin reenviar la solicitud.");
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<BillingDocumentResponse>.Success(BillingDocumentMapper.ToResponse(document));
    }
}

public sealed class GetArcaConnectionStatusUseCase(IArcaElectronicInvoiceGateway gateway) : IGetArcaConnectionStatusUseCase
{
    public async Task<AppResult<ArcaConnectionStatus>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        AppResult<ArcaConnectionStatus>.Success(await gateway.CheckConnectionAsync(cancellationToken));
}

internal static class BillingDocumentMapper
{
    public static BillingDocumentResponse ToResponse(BillingDocument document) => new(
        document.Id,
        document.OrderId,
        document.PaymentId,
        document.RelatedDocumentId,
        document.Category.ToString(),
        document.Type.ToString(),
        document.Status.ToString(),
        document.Currency,
        document.IssuerBusinessName,
        document.RecipientName,
        document.RecipientEmail,
        document.RecipientAddress,
        document.Subtotal,
        document.DiscountAmount,
        document.ShippingAmount,
        document.Total,
        document.PointOfSale,
        document.DocumentNumber,
        document.AuthorizationProvider,
        document.Cae,
        document.CaeExpiresOn,
        document.RejectionCode,
        document.RejectionReason,
        document.CreatedAtUtc,
        document.AuthorizedAtUtc,
        document.Items.OrderBy(x => x.Id).Select(x => new BillingDocumentItemResponse(
            x.Id, x.OrderItemId, x.Description, x.Quantity, x.UnitPrice, x.DiscountAmount, x.TotalAmount)).ToList());
}
