using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HttpWebhookTests : IAsyncLifetime
{
    private const string Secret = "webhook-http-test-secret";
    private readonly FakeWebhookGateway _gateway = new();
    private GymShopWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new GymShopWebApplicationFactory(new Dictionary<string, string?>
        {
            ["MercadoPago:Enabled"] = "true",
            ["MercadoPago:AccessToken"] = "TEST_ONLY_NOT_REAL",
            ["MercadoPago:WebhookSecret"] = Secret
        }, services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(_gateway);
        });
        await _factory.InitializeAsync();
        _client = _factory.CreateHttpsClient();
        var user = await _factory.SeedUserAsync("webhook-owner@test.com", "User");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
        var product = new Product { Name = "Webhook Product", Price = 100, Stock = 2, IsActive = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var order = new Order { UserId = user.Id, Total = 100, Status = OrderStatus.Pending, ShippingAddress = "Webhook Address" };
        order.Items.Add(new OrderItem { ProductId = product.Id, Product = product, ProductName = product.Name, UnitPrice = 100, Quantity = 1, Subtotal = 100 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.Payments.Add(new Payment
        {
            OrderId = order.Id, Provider = "MercadoPago", ExternalReference = $"order-{order.Id}",
            Amount = 100, Currency = "ARS", Status = PaymentStatus.Pending
        });
        await db.SaveChangesAsync();
        _gateway.ExternalReference = $"order-{order.Id}";
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Unsigned_webhook_is_rejected_and_signed_webhook_updates_payment()
    {
        const string paymentId = "mp-pay-http";
        var unsigned = await _client.PostAsJsonAsync($"/api/payments/mercadopago/webhook?data.id={paymentId}", new { data = new { id = paymentId } });
        using var signedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/mercadopago/webhook?data.id={paymentId}")
        {
            Content = JsonContent.Create(new { data = new { id = paymentId } })
        };
        signedRequest.Headers.Add("x-request-id", "request-http-1");
        signedRequest.Headers.Add("x-signature", CreateSignature(paymentId, "request-http-1", CurrentTimestamp()));
        var signed = await _client.SendAsync(signedRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal(1, _gateway.GetCalls);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Signed_webhook_with_query_id_and_empty_body_updates_payment()
    {
        const string paymentId = "mp-query-only-http";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/mercadopago/webhook?data.id={paymentId}");
        request.Headers.Add("x-request-id", "request-query-only-1");
        request.Headers.Add("x-signature", CreateSignature(paymentId, "request-query-only-1", CurrentTimestamp()));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _gateway.GetCalls);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unsigned_webhook_with_query_id_and_empty_body_is_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/mercadopago/webhook?data.id=mp-unsigned-query");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _gateway.GetCalls);
    }

    [Fact]
    public async Task Signed_webhook_with_body_id_and_no_query_updates_payment()
    {
        const string paymentId = "mp-body-only-http";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/mercadopago/webhook")
        {
            Content = JsonContent.Create(new { data = new { id = paymentId } })
        };
        request.Headers.Add("x-request-id", "request-body-only-1");
        request.Headers.Add("x-signature", CreateSignature(paymentId, "request-body-only-1", CurrentTimestamp()));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _gateway.GetCalls);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Legacy_payment_ipn_is_verified_with_provider_and_updates_payment()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/mercadopago/webhook?topic=payment&id=123456789")
        {
            Content = JsonContent.Create(new { resource = "https://api.mercadopago.com/v1/payments/123456789", topic = "payment" })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _gateway.GetCalls);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Replayed_signed_webhook_is_acknowledged_without_reprocessing()
    {
        const string paymentId = "mp-replay-http";
        const string requestId = "request-replay-1";
        var signature = CreateSignature(paymentId, requestId, CurrentTimestamp());

        async Task<HttpResponseMessage> SendAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/mercadopago/webhook?data.id={paymentId}");
            request.Headers.Add("x-request-id", requestId);
            request.Headers.Add("x-signature", signature);
            return await _client.SendAsync(request);
        }

        using var first = await SendAsync();
        using var replay = await SendAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(1, _gateway.GetCalls);
    }

    [Theory]
    [InlineData("merchant_order", "123456789")]
    [InlineData("payment", "not-numeric")]
    public async Task Legacy_ipn_requires_payment_topic_and_numeric_id(string topic, string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/mercadopago/webhook?topic={topic}&id={id}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _gateway.GetCalls);
    }

    private static string CreateSignature(string dataId, string requestId, string timestamp)
    {
        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var value = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return $"ts={timestamp},v1={value}";
    }

    private static string CurrentTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class FakeWebhookGateway : IPaymentGateway
    {
        public string ExternalReference { get; set; } = string.Empty;
        public int GetCalls { get; private set; }
        public bool CanHandle(string provider) => provider.Equals("MercadoPago", StringComparison.OrdinalIgnoreCase);
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(new ProviderPaymentResult(providerPaymentId, ExternalReference, "approved", 100, "ARS", null));
        }
    }
}
