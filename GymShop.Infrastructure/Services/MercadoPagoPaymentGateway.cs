using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Services;

public class MercadoPagoPaymentGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly MercadoPagoOptions _options;

    public MercadoPagoPaymentGateway(HttpClient httpClient, IOptions<MercadoPagoOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool CanHandle(string provider) =>
        _options.Enabled && string.Equals(provider, "MercadoPago", StringComparison.OrdinalIgnoreCase);

    public async Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
        => await CreatePreferenceCoreAsync(order, idempotencyKey, externalReference,
            _options.SuccessUrl, _options.FailureUrl, _options.PendingUrl, "{orderId}", cancellationToken);

    public async Task<PaymentPreferenceResult> CreatePreferenceAsync(CheckoutSession checkout, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
    {
        var projection = new Order { Id = checkout.Id, Total = checkout.Total, ShippingCost = checkout.ShippingCost };
        foreach (var item in checkout.Items)
            projection.Items.Add(new OrderItem { ProductId = item.ProductId, ProductName = item.ProductName, UnitPrice = item.UnitPrice, Quantity = item.Quantity, Subtotal = item.Subtotal });
        return await CreatePreferenceCoreAsync(projection, idempotencyKey, externalReference,
            _options.CheckoutSuccessUrl, _options.CheckoutFailureUrl, _options.CheckoutPendingUrl, "{checkoutId}", cancellationToken);
    }

    private async Task<PaymentPreferenceResult> CreatePreferenceCoreAsync(Order order, string? idempotencyKey, string? externalReference,
        string? successTemplate, string? failureTemplate, string? pendingTemplate, string placeholder, CancellationToken cancellationToken)
    {
        if (decimal.Round(order.Total, 2, MidpointRounding.AwayFromZero) <= 0)
        {
            throw new PaymentGatewayException("Mercado Pago no admite preferencias con importe cero.");
        }

        ConfigureAuthorization();

        var notificationUrl = _options.NotificationUrl;
        var successUrl = FormatUrl(successTemplate, placeholder, order.Id);
        var failureUrl = FormatUrl(failureTemplate, placeholder, order.Id);
        var pendingUrl = FormatUrl(pendingTemplate, placeholder, order.Id);

        var payload = new Dictionary<string, object?>
        {
            ["items"] = BuildPreferenceItems(order),
            ["external_reference"] = string.IsNullOrWhiteSpace(externalReference) ? $"order-{order.Id}" : externalReference
        };

        if (IsPublicCallbackUrl(notificationUrl))
        {
            payload["notification_url"] = notificationUrl;
        }

        var backUrls = new Dictionary<string, string>();
        if (IsPublicCallbackUrl(successUrl))
        {
            backUrls["success"] = successUrl!;
        }

        if (IsPublicCallbackUrl(failureUrl))
        {
            backUrls["failure"] = failureUrl!;
        }

        if (IsPublicCallbackUrl(pendingUrl))
        {
            backUrls["pending"] = pendingUrl!;
        }

        if (backUrls.Count > 0)
        {
            payload["back_urls"] = backUrls;
        }


        using var request = new HttpRequestMessage(HttpMethod.Post, "checkout/preferences")
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymentGatewayException($"Mercado Pago rechazo la preferencia: {(int)response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var preferenceId = root.GetProperty("id").GetString();
        var checkoutUrl = ReadString(root, _options.UseSandboxInitPoint ? "sandbox_init_point" : "init_point") ?? ReadString(root, "init_point");

        if (string.IsNullOrWhiteSpace(preferenceId) || string.IsNullOrWhiteSpace(checkoutUrl))
        {
            throw new PaymentGatewayException("Mercado Pago no devolvio preference id o checkout url.");
        }

        return new PaymentPreferenceResult("MercadoPago", preferenceId, checkoutUrl);
    }

    private static List<Dictionary<string, object>> BuildPreferenceItems(Order order)
    {
        var total = decimal.Round(order.Total, 2, MidpointRounding.AwayFromZero);
        var shipping = decimal.Round(order.ShippingCost, 2, MidpointRounding.AwayFromZero);
        var merchandiseTotal = total - shipping;
        var sourceLines = order.Items
            .Select(item => new
            {
                Item = item,
                Subtotal = item.Subtotal > 0 ? item.Subtotal : item.UnitPrice * item.Quantity
            })
            .Where(line => line.Subtotal > 0)
            .ToList();
        var sourceSubtotal = sourceLines.Sum(line => line.Subtotal);
        var items = new List<Dictionary<string, object>>();
        var remaining = merchandiseTotal;

        for (var index = 0; index < sourceLines.Count && remaining > 0; index++)
        {
            var line = sourceLines[index];
            var amount = index == sourceLines.Count - 1
                ? remaining
                : decimal.Round(merchandiseTotal * line.Subtotal / sourceSubtotal, 2, MidpointRounding.AwayFromZero);
            amount = Math.Min(amount, remaining);
            remaining -= amount;
            if (amount <= 0) continue;

            items.Add(new Dictionary<string, object>
            {
                ["id"] = line.Item.ProductId.ToString(),
                ["title"] = line.Item.Quantity == 1 ? line.Item.ProductName : $"{line.Item.Quantity} x {line.Item.ProductName}",
                ["quantity"] = 1,
                ["currency_id"] = "ARS",
                ["unit_price"] = amount
            });
        }

        if (remaining > 0)
        {
            items.Add(new Dictionary<string, object>
            {
                ["id"] = $"order-{order.Id}",
                ["title"] = $"Productos del pedido #{order.Id}",
                ["quantity"] = 1,
                ["currency_id"] = "ARS",
                ["unit_price"] = remaining
            });
        }

        if (shipping > 0)
        {
            items.Add(new Dictionary<string, object>
            {
                ["id"] = $"shipping-{order.Id}",
                ["title"] = "Envio",
                ["quantity"] = 1,
                ["currency_id"] = "ARS",
                ["unit_price"] = shipping
            });
        }

        return items;
    }

    public async Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        ConfigureAuthorization();

        using var response = await _httpClient.GetAsync($"v1/payments/{providerPaymentId}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymentGatewayException($"Mercado Pago no devolvio el pago: {(int)response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        return new ProviderPaymentResult(
            ReadString(root, "id") ?? providerPaymentId,
            ReadString(root, "external_reference") ?? string.Empty,
            ReadString(root, "status") ?? string.Empty,
            ReadDecimal(root, "transaction_amount"),
            ReadString(root, "currency_id") ?? "ARS",
            ReadString(root, "status_detail")
        );
    }

    public async Task ExpirePreferenceAsync(string providerPreferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPreferenceId))
        {
            throw new PaymentGatewayException("El id de la preferencia de Mercado Pago es obligatorio.");
        }

        ConfigureAuthorization();
        var expirationDateTo = DateTimeOffset.UtcNow.AddSeconds(-1);
        var payload = new
        {
            expires = true,
            expiration_date_from = expirationDateTo.AddDays(-1),
            expiration_date_to = expirationDateTo
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"checkout/preferences/{Uri.EscapeDataString(providerPreferenceId.Trim())}")
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PaymentGatewayException(
                $"Mercado Pago no pudo invalidar la preferencia: {(int)response.StatusCode}.");
        }
    }

    public async Task<bool> RefundPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
            throw new PaymentGatewayException("El id del pago de Mercado Pago es obligatorio para devolverlo.");

        ConfigureAuthorization();
        using var response = await _httpClient.PostAsync(
            $"v1/payments/{Uri.EscapeDataString(providerPaymentId.Trim())}/refunds",
            content: null,
            cancellationToken);
        if (response.IsSuccessStatusCode) return true;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new PaymentGatewayException($"Mercado Pago no pudo devolver el pago: {(int)response.StatusCode} {body}");
    }

    private void ConfigureAuthorization()
    {
        var accessToken = _options.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new PaymentGatewayException("MercadoPago:AccessToken no esta configurado.");
        }

        _httpClient.BaseAddress ??= new Uri("https://api.mercadopago.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string? FormatUrl(string? template, string placeholder, int id)
    {
        return string.IsNullOrWhiteSpace(template) ? null : template.Replace(placeholder, id.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicCallbackUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https" &&
               !uri.IsLoopback &&
               !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal ReadDecimal(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var amount)
            ? amount
            : 0;
    }
}



