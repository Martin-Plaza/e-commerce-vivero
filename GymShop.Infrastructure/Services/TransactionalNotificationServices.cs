using System.Globalization;
using System.Text.Encodings.Web;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Services;

public sealed class TransactionalNotificationProcessor(
    GymShopDbContext db,
    ITransactionalEmailSender sender,
    IReceiptPdfRenderer receiptRenderer,
    IBillingProfile billingProfile,
    IOptions<EmailOptions> options,
    IOptions<BankTransferOptions> bankTransferOptions,
    TimeProvider timeProvider,
    ILogger<TransactionalNotificationProcessor> logger)
{
    private const int MaxAttempts = 8;
    private readonly EmailOptions _options = options.Value;
    private readonly BankTransferOptions _bankTransfer = bankTransferOptions.Value;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        List<NotificationOutboxMessage> claimed;
        if (!db.Database.IsRelational())
        {
            claimed = await db.NotificationOutboxMessages
                .Where(x => x.Status == NotificationDeliveryStatus.Pending && x.NextAttemptAtUtc <= now ||
                            x.Status == NotificationDeliveryStatus.Processing && x.LockedUntilUtc < now)
                .OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(cancellationToken);
            Claim(claimed, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            claimed = db.Database.IsNpgsql()
                ? await db.NotificationOutboxMessages.FromSqlInterpolated($$"""
                    SELECT * FROM "NotificationOutboxMessages"
                    WHERE ("Status" = 'Pending' AND "NextAttemptAtUtc" <= {{now}})
                       OR ("Status" = 'Processing' AND "LockedUntilUtc" < {{now}})
                    ORDER BY "CreatedAtUtc"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 20
                    """).ToListAsync(cancellationToken)
                : await db.NotificationOutboxMessages
                    .Where(x => x.Status == NotificationDeliveryStatus.Pending && x.NextAttemptAtUtc <= now ||
                                x.Status == NotificationDeliveryStatus.Processing && x.LockedUntilUtc < now)
                    .OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(cancellationToken);
            Claim(claimed, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var claimedMessage in claimed)
            await DeliverAsync(claimedMessage.Id, cancellationToken);
        return claimed.Count;
    }

    private static void Claim(IEnumerable<NotificationOutboxMessage> messages, DateTime now)
    {
        foreach (var message in messages)
        {
            message.Status = NotificationDeliveryStatus.Processing;
            message.LockedUntilUtc = now.AddMinutes(2);
            message.AttemptCount++;
        }
    }

    private async Task DeliverAsync(Guid id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var message = await db.NotificationOutboxMessages.SingleAsync(x => x.Id == id, cancellationToken);
        var email = await ComposeAsync(message, cancellationToken);
        if (email is null)
        {
            message.Status = NotificationDeliveryStatus.DeadLetter;
            message.LastFailureType = "MissingBusinessData";
            message.LockedUntilUtc = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Transactional notification could not be composed. NotificationType {NotificationType}; MessageId {MessageId}.", message.Type, message.Id);
            return;
        }

        var result = await sender.SendAsync(email, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (result.AcceptedByProvider)
        {
            message.Status = NotificationDeliveryStatus.Sent;
            message.SentAtUtc = now;
            message.LastFailureType = null;
        }
        else
        {
            message.LastFailureType = result.FailureType?.ToString() ?? "NotAccepted";
            message.Status = message.AttemptCount >= MaxAttempts ? NotificationDeliveryStatus.DeadLetter : NotificationDeliveryStatus.Pending;
            message.NextAttemptAtUtc = now.AddMinutes(Math.Min(360, Math.Pow(2, message.AttemptCount - 1)));
        }
        message.LockedUntilUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<TransactionalEmailMessage?> ComposeAsync(NotificationOutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.OrderId is null) return null;
        var order = await db.Orders.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == message.OrderId, cancellationToken);
        if (order is null) return null;
        var recipientEmail = order.User?.Email ?? order.GuestEmail;
        var customerName = order.User?.Name ?? order.GuestFirstName;
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(customerName)) return null;
        var name = HtmlEncoder.Default.Encode(customerName);
        var orderUrl = order.UserId.HasValue ? Link("/ordenes") : order.GuestAccessToken.HasValue ? Link($"/checkout/orden/{order.Id}?access={order.GuestAccessToken.Value:D}") : null;
        var amount = order.Total.ToString("C", CultureInfo.GetCultureInfo("es-AR"));
        var (subject, heading, text) = message.Type switch
        {
            TransactionalNotificationType.OrderCreated when !order.UserId.HasValue && order.Status == OrderStatus.Pending =>
                ($"Datos para transferir · pedido #{order.Id}", "Pedido pendiente de transferencia", GuestTransferText(order, amount)),
            TransactionalNotificationType.OrderCreated when !order.UserId.HasValue =>
                ($"Recibimos tu pedido #{order.Id}", "Pedido recibido", $"Registramos tu pedido por {amount}. Podés consultar su estado desde el enlace privado de este email."),
            TransactionalNotificationType.OrderCreated => ($"Recibimos tu pedido #{order.Id}", "Pedido recibido", $"Registramos tu pedido por {amount}. Podés consultar su estado y continuar con el pago desde Mis órdenes."),
            TransactionalNotificationType.PaymentApproved => ($"Pago aprobado para el pedido #{order.Id}", "Pago aprobado", $"Confirmamos el pago de {amount}. Adjuntamos la constancia interna de tu compra y ya podemos comenzar a preparar tu pedido."),
            TransactionalNotificationType.PaymentRejected => ($"No se aprobó el pago del pedido #{order.Id}", "Pago no aprobado", "El proveedor no aprobó el intento de pago. Podés revisar el pedido e intentar nuevamente mientras continúe pendiente."),
            TransactionalNotificationType.OrderPreparing => ($"Estamos preparando tu pedido #{order.Id}", "Pedido en preparación", "Tu compra ya está siendo preparada."),
            TransactionalNotificationType.OrderShipped => ($"Tu pedido #{order.Id} fue enviado", "Pedido enviado", TrackingText(order)),
            TransactionalNotificationType.OrderReadyForPickup => ($"Tu pedido #{order.Id} está listo para retirar", "Listo para retirar", PickupText(order)),
            TransactionalNotificationType.PaymentRefunded => ($"Reembolso confirmado para el pedido #{order.Id}", "Reembolso confirmado", $"El proveedor confirmó el reembolso de {amount}."),
            TransactionalNotificationType.BillingDocumentAvailable => await BillingTextAsync(message, order.Id, cancellationToken),
            TransactionalNotificationType.OrderExpired => ($"Venció el pedido #{order.Id}", "Pedido vencido", "El plazo para realizar la transferencia terminó y el pedido fue dado de baja. No realices pagos usando esta referencia."),
            TransactionalNotificationType.StockUnavailableAfterPayment => ($"Necesitamos resolver el pedido #{order.Id}", "Producto sin stock después del pago", "Recibimos el pago, pero uno de los productos ya no está disponible. Te contactaremos para ofrecerte un cambio o gestionar la devolución."),
            _ => throw new ArgumentOutOfRangeException()
        };
        var safeText = HtmlEncoder.Default.Encode(text);
        var buttonLabel = order.UserId.HasValue ? "Ver mis órdenes" : "Ver pedido";
        var button = orderUrl is null ? string.Empty : $"<p><a href=\"{HtmlEncoder.Default.Encode(orderUrl)}\" style=\"display:inline-block;padding:12px 18px;background:#c7ff2f;color:#111;text-decoration:none;font-weight:700\">{buttonLabel}</a></p>";
        var html = $"<main style=\"font-family:Arial,sans-serif;max-width:620px;margin:auto;color:#171b18\"><h1>{HtmlEncoder.Default.Encode(heading)}</h1><p>Hola {name},</p><p>{safeText}</p>{button}<p style=\"color:#667085\">Este es un mensaje automático de {_options.FromName}.</p></main>";
        IReadOnlyList<EmailAttachment>? attachments = message.Type == TransactionalNotificationType.PaymentApproved
            ? [BuildPurchaseReceipt(order, message.PaymentId)]
            : null;
        return new TransactionalEmailMessage(message.Type.ToString(), recipientEmail, subject, html, $"notification/{message.Id:N}", attachments);
    }

    private EmailAttachment BuildPurchaseReceipt(Order order, int? paymentId)
    {
        var payment = order.Payments.FirstOrDefault(x => x.Id == paymentId)
            ?? order.Payments.Where(x => x.Status == PaymentStatus.Approved)
                .OrderByDescending(x => x.PaidAt ?? x.UpdatedAt ?? x.CreatedAt).FirstOrDefault();
        var issuedAt = payment?.PaidAt ?? timeProvider.GetUtcNow().UtcDateTime;
        var document = new BillingDocument
        {
            OrderId = order.Id,
            PaymentId = payment?.Id,
            Category = BillingDocumentCategory.Receipt,
            Type = BillingDocumentType.PurchaseReceipt,
            Status = BillingDocumentStatus.Authorized,
            Currency = payment?.Currency ?? "ARS",
            IssuerBusinessName = billingProfile.BusinessName.Trim(),
            IssuerCuit = billingProfile.Cuit.Trim(),
            IssuerTaxCondition = billingProfile.TaxCondition,
            IssuerFiscalAddress = billingProfile.FiscalAddress.Trim(),
            IssuerGrossIncomeNumber = billingProfile.GrossIncomeNumber.Trim(),
            IssuerActivityStartDate = billingProfile.ActivityStartDate,
            RecipientName = order.User is null ? $"{order.GuestFirstName} {order.GuestLastName}".Trim() : $"{order.User.Name} {order.User.LastName}".Trim(),
            RecipientDocumentType = FiscalIdentityDocumentType.None,
            RecipientTaxCondition = RecipientTaxCondition.ConsumerFinal,
            RecipientEmail = order.User?.Email ?? order.GuestEmail,
            RecipientAddress = order.ShippingAddress,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            ShippingAmount = order.ShippingCost,
            Total = order.Total,
            AuthorizationProvider = "Internal",
            AuthorizedAtUtc = issuedAt,
            CreatedAtUtc = issuedAt
        };
        foreach (var item in order.Items.OrderBy(x => x.Id))
            document.Items.Add(new BillingDocumentItem
            {
                OrderItemId = item.Id,
                Description = string.IsNullOrWhiteSpace(item.VariantSku) ? item.ProductName : $"{item.ProductName} ({item.VariantSku})",
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NetAmount = item.Subtotal,
                TotalAmount = item.Subtotal
            });
        return new EmailAttachment($"pedido-{order.Id}-comprobante.pdf", "application/pdf", receiptRenderer.Render(document));
    }

    private async Task<(string Subject, string Heading, string Text)> BillingTextAsync(NotificationOutboxMessage message, int orderId, CancellationToken cancellationToken)
    {
        if (message.BillingDocumentId is null) return ($"Comprobante disponible para el pedido #{orderId}", "Comprobante disponible", "Tu comprobante ya está disponible en Mis órdenes.");
        var document = await db.BillingDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == message.BillingDocumentId, cancellationToken);
        var label = document?.Category switch
        {
            BillingDocumentCategory.Invoice => "factura",
            BillingDocumentCategory.CreditNote => "nota de crédito",
            _ => "comprobante"
        };
        return ($"Tu {label} del pedido #{orderId} está disponible", "Comprobante disponible", $"Tu {label} ya está disponible para visualizar o descargar desde Mis órdenes.");
    }

    private string? Link(string path) => string.IsNullOrWhiteSpace(_options.PublicAppUrl)
        ? null
        : $"{_options.PublicAppUrl.TrimEnd('/')}{path}";
    private string GuestTransferText(Order order, string amount)
    {
        var deadline = (order.ExpiresAtUtc ?? order.CreatedAt.AddHours(_bankTransfer.PendingOrderLifetimeHours)).ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR"));
        var bank = string.IsNullOrWhiteSpace(_bankTransfer.BankName) ? "banco a confirmar" : _bankTransfer.BankName;
        var alias = string.IsNullOrWhiteSpace(_bankTransfer.Alias) ? "alias a confirmar" : _bankTransfer.Alias;
        var cbu = string.IsNullOrWhiteSpace(_bankTransfer.Cbu) ? "CBU a confirmar" : _bankTransfer.Cbu;
        var reference = order.Payments
            .Where(x => x.Provider == "BankTransfer")
            .OrderByDescending(x => x.Id)
            .Select(x => x.ExternalReference)
            .FirstOrDefault() ?? order.Id.ToString(CultureInfo.InvariantCulture);
        return $"Registramos tu pedido por {amount}. Transferí antes del {deadline} e indicá la referencia {reference}. Datos: {bank}; alias {alias}; CBU {cbu}. La disponibilidad se confirma al acreditar la transferencia. Si algún producto no está disponible, te contactaremos para ofrecerte un cambio o gestionar la devolución.";
    }
    private static string TrackingText(Order order) => string.IsNullOrWhiteSpace(order.TrackingNumber)
        ? "Tu pedido salió del comercio y está en camino."
        : $"Tu pedido está en camino con {order.Carrier}. Número de seguimiento: {order.TrackingNumber}.";
    private static string PickupText(Order order) => string.IsNullOrWhiteSpace(order.PickupAddress)
        ? "Tu pedido ya está preparado para retirar."
        : $"Tu pedido está listo para retirar en {order.PickupAddress}. {order.PickupHours}";
}

public sealed class TransactionalNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    ILogger<TransactionalNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.TransactionalNotificationsEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TransactionalNotificationProcessor>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Transactional notification worker iteration failed.");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
