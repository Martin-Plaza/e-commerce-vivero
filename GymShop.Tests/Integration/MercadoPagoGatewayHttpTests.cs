using System.Net;
using System.Text;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Gateway")]
public sealed class MercadoPagoGatewayHttpTests
{
    public static TheoryData<decimal, decimal, decimal, decimal> PreferenceTotals => new()
    {
        { 100m, 0m, 0m, 100m },
        { 100m, 0m, 25m, 125m },
        { 100m, 10m, 0m, 90m },
        { 99.99m, 5m, 10.01m, 105m }
    };

    [Fact]
    public async Task Successful_preference_sends_authorization_and_idempotency_key()
    {
        string? requestBody = null;
        var handler = new StubHttpHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Created, """{"id":"pref-http-1","sandbox_init_point":"https://sandbox.example/checkout"}""");
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.CreatePreferenceAsync(CreateOrder(), "idem-http-1", "order-42-payment-99");

        Assert.Equal("pref-http-1", result.ProviderPreferenceId);
        Assert.Equal("https://sandbox.example/checkout", result.CheckoutUrl);
        Assert.Equal("idem-http-1", handler.Requests.Single().Headers.GetValues("X-Idempotency-Key").Single());
        Assert.Equal("Bearer", handler.Requests.Single().Headers.Authorization?.Scheme);
        Assert.Contains("order-42-payment-99", requestBody);
        Assert.DoesNotContain("\"payer\"", requestBody);
    }

    [Theory]
    [MemberData(nameof(PreferenceTotals))]
    public async Task Preference_items_add_up_exactly_to_order_total(
        decimal subtotal, decimal discount, decimal shipping, decimal expectedTotal)
    {
        string? requestBody = null;
        var handler = new StubHttpHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Created, """{"id":"pref-total","sandbox_init_point":"https://sandbox.example/checkout"}""");
        });
        var order = CreateOrder(subtotal, discount, shipping, expectedTotal);

        await CreateGateway(handler).CreatePreferenceAsync(order, "idem-total", "order-42-payment-99");

        using var payload = JsonDocument.Parse(requestBody!);
        var items = payload.RootElement.GetProperty("items").EnumerateArray().ToList();
        var chargedTotal = items.Sum(item =>
            item.GetProperty("unit_price").GetDecimal() * item.GetProperty("quantity").GetInt32());
        Assert.Equal(decimal.Round(order.Total, 2, MidpointRounding.AwayFromZero), chargedTotal);
        Assert.All(items, item => Assert.Equal("ARS", item.GetProperty("currency_id").GetString()));
        Assert.Contains(items, item => item.GetProperty("title").GetString()!.Contains("Producto"));
        Assert.Equal(shipping > 0, items.Any(item => item.GetProperty("title").GetString() == "Envio"));
    }

    [Fact]
    public async Task Http_4xx_and_5xx_are_reported_as_gateway_errors()
    {
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError })
        {
            var gateway = CreateGateway(new StubHttpHandler(_ => Json(status, "{\"message\":\"provider error\"}")));
            var error = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.CreatePreferenceAsync(CreateOrder(), "idem-error"));
            Assert.Contains(((int)status).ToString(), error.Message);
        }
    }

    [Fact]
    public async Task Zero_total_is_rejected_before_calling_Mercado_Pago()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var order = CreateOrder(subtotal: 100m, discount: 100m, shipping: 0m, total: 0m);

        var error = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            CreateGateway(handler).CreatePreferenceAsync(order, "idem-free"));

        Assert.Contains("importe cero", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Timeout_and_invalid_json_are_exposed_without_fake_success()
    {
        var timeoutGateway = CreateGateway(new StubHttpHandler(_ => throw new TaskCanceledException("timeout")));
        await Assert.ThrowsAsync<TaskCanceledException>(() => timeoutGateway.CreatePreferenceAsync(CreateOrder(), "idem-timeout"));

        var invalidGateway = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.OK, "not-json")));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => invalidGateway.CreatePreferenceAsync(CreateOrder(), "idem-json"));
    }

    [Fact]
    public async Task Retried_call_preserves_idempotency_key_and_refunded_response_is_parsed()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, """{"id":"pref-retry","sandbox_init_point":"https://sandbox.example/retry"}""")
            : Json(HttpStatusCode.OK, """{"id":"pay-1","external_reference":"order-42","status":"refunded","transaction_amount":100,"currency_id":"ARS"}"""));
        var gateway = CreateGateway(handler);

        await gateway.CreatePreferenceAsync(CreateOrder(), "stable-idem-key");
        await gateway.CreatePreferenceAsync(CreateOrder(), "stable-idem-key");
        var refunded = await gateway.GetPaymentAsync("pay-1");

        Assert.Equal(2, handler.Requests.Count(x => x.Method == HttpMethod.Post));
        Assert.All(handler.Requests.Where(x => x.Method == HttpMethod.Post), request =>
            Assert.Equal("stable-idem-key", request.Headers.GetValues("X-Idempotency-Key").Single()));
        Assert.Equal("refunded", refunded.Status);
        Assert.Equal("order-42", refunded.ExternalReference);
    }

    [Fact]
    public async Task Expire_preference_sends_an_already_expired_validity_window()
    {
        string? requestBody = null;
        var handler = new StubHttpHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, "{}");
        });

        await CreateGateway(handler).ExpirePreferenceAsync("pref-http-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/checkout/preferences/pref-http-1", request.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        using var payload = JsonDocument.Parse(requestBody!);
        Assert.True(payload.RootElement.GetProperty("expires").GetBoolean());
        var expiration = payload.RootElement.GetProperty("expiration_date_to").GetDateTimeOffset();
        Assert.True(expiration <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Expire_preference_reports_provider_failure()
    {
        var gateway = CreateGateway(new StubHttpHandler(_ => Json(HttpStatusCode.BadRequest, "{}")));

        var error = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            gateway.ExpirePreferenceAsync("pref-http-1"));

        Assert.Contains("400", error.Message);
    }

    private static MercadoPagoPaymentGateway CreateGateway(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.test/") };
        return new MercadoPagoPaymentGateway(client, Options.Create(new MercadoPagoOptions
        {
            Enabled = true, AccessToken = "TEST_ONLY_NOT_REAL", UseSandboxInitPoint = true
        }));
    }

    private static Order CreateOrder(decimal subtotal = 100m, decimal discount = 0m, decimal shipping = 0m, decimal? total = null)
    {
        var user = new User { Email = "payer@test.com", Name = "Payer", PasswordHash = "not-used" };
        var order = new Order
        {
            Id = 42,
            User = user,
            Subtotal = subtotal,
            DiscountAmount = discount,
            ShippingCost = shipping,
            Total = total ?? subtotal - discount + shipping,
            ShippingAddress = "Test"
        };
        var firstSubtotal = decimal.Round(subtotal / 3m, 2, MidpointRounding.AwayFromZero);
        var secondSubtotal = subtotal - firstSubtotal;
        order.Items.Add(new OrderItem { ProductId = 1, ProductName = "Producto A", Quantity = 1, UnitPrice = firstSubtotal, Subtotal = firstSubtotal });
        order.Items.Add(new OrderItem { ProductId = 2, ProductName = "Producto B", Quantity = 1, UnitPrice = secondSubtotal, Subtotal = secondSubtotal });
        return order;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
