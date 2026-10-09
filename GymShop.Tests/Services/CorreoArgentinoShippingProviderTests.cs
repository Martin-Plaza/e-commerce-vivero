using System.Net;
using System.Text;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymShop.Tests.Services;

public sealed class CorreoArgentinoShippingProviderTests
{
    [Fact]
    public async Task Quote_authenticates_maps_distinct_packages_and_aggregates_common_services()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var validTo = now.AddMinutes(8);
        var handler = new RecordingHandler((request, rateCall) => request.RequestUri!.AbsolutePath switch
        {
            "/micorreo/v1/token" => Json(HttpStatusCode.OK, """{"token":"cached-token"}"""),
            "/micorreo/v1/rates" when rateCall == 1 => Json(HttpStatusCode.OK, Rates(validTo,
                "{\"deliveredType\":\"D\",\"productType\":\"CP\",\"productName\":\"Clásico\",\"price\":100}," +
                "{\"deliveredType\":\"D\",\"productType\":\"EP\",\"productName\":\"Expreso\",\"price\":200}")),
            "/micorreo/v1/rates" => Json(HttpStatusCode.OK, Rates(validTo,
                """{"deliveredType":"D","productType":"CP","productName":"Clásico","price":150}""")),
            _ => Json(HttpStatusCode.NotFound, "{}")
        });
        var provider = CreateProvider(handler, now);
        var request = new ShippingQuoteRequest(
            Address("S2000ABC"),
            Address("A4400XYZ"),
            [
                new ShippingPackage(1000, 10.1m, 20.1m, 30.1m, 30_000, 2),
                new ShippingPackage(2500, 40, 35, 20, 50_000)
            ],
            ShippingDeliveryType.HomeDelivery);

        var quote = Assert.Single(await provider.QuoteAsync(request));

        Assert.Equal(CorreoArgentinoShippingProvider.ProviderCode, quote.ProviderCode);
        Assert.Equal("cp-home", quote.ServiceCode);
        Assert.Equal("Clásico", quote.ServiceName);
        Assert.Equal(350m, quote.Price);
        Assert.Equal(now.AddDays(2), quote.EstimatedDeliveryFrom);
        Assert.Equal(now.AddDays(5), quote.EstimatedDeliveryTo);
        Assert.Equal(validTo, quote.ExpiresAt);
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(2, handler.RateCalls);

        var tokenRequest = Assert.Single(handler.Requests, item => item.Path.EndsWith("/token", StringComparison.Ordinal));
        Assert.Equal("Basic", tokenRequest.AuthorizationScheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("api-user:api-password")), tokenRequest.AuthorizationValue);

        var rateRequests = handler.Requests.Where(item => item.Path.EndsWith("/rates", StringComparison.Ordinal)).ToList();
        Assert.All(rateRequests, item =>
        {
            Assert.Equal("Bearer", item.AuthorizationScheme);
            Assert.Equal("cached-token", item.AuthorizationValue);
        });
        using var firstBody = JsonDocument.Parse(rateRequests[0].Body!);
        var root = firstBody.RootElement;
        Assert.Equal("customer-id", root.GetProperty("customerId").GetString());
        Assert.Equal("S2000ABC", root.GetProperty("postalCodeOrigin").GetString());
        Assert.Equal("A4400XYZ", root.GetProperty("postalCodeDestination").GetString());
        Assert.Equal("D", root.GetProperty("deliveredType").GetString());
        var dimensions = root.GetProperty("dimensions");
        Assert.Equal(1000, dimensions.GetProperty("weight").GetInt32());
        Assert.Equal(31, dimensions.GetProperty("height").GetInt32());
        Assert.Equal(21, dimensions.GetProperty("width").GetInt32());
        Assert.Equal(11, dimensions.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task Quote_reuses_cached_token_between_requests()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, """{"token":"cached-token"}""")
            : Json(HttpStatusCode.OK, Rates(now.AddMinutes(10),
                """{"deliveredType":"D","productType":"CP","productName":"Clásico","price":100}""")));
        var provider = CreateProvider(handler, now);
        var request = Request([new ShippingPackage(1000, 10, 10, 10, 1000)]);

        Assert.Single(await provider.QuoteAsync(request));
        Assert.Single(await provider.QuoteAsync(request));

        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(2, handler.RateCalls);
    }

    [Fact]
    public async Task Quote_refreshes_token_once_after_unauthorized_response()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, rateCall) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $"{{\"token\":\"token-{request.Headers.Authorization?.Parameter}\"}}");
            return rateCall == 1
                ? Json(HttpStatusCode.Unauthorized, "{}")
                : Json(HttpStatusCode.OK, Rates(now.AddMinutes(10),
                    """{"deliveredType":"D","productType":"CP","productName":"Clásico","price":100}"""));
        });
        var provider = CreateProvider(handler, now);

        Assert.Single(await provider.QuoteAsync(Request([new ShippingPackage(1000, 10, 10, 10, 1000)])));

        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(2, handler.RateCalls);
    }

    [Theory]
    [InlineData(25001, 10, 10, 10)]
    [InlineData(1000, 151, 10, 10)]
    public async Task Quote_does_not_call_api_when_package_exceeds_provider_limits(
        int weight,
        decimal length,
        decimal width,
        decimal height)
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var provider = CreateProvider(handler, DateTimeOffset.UtcNow);

        var result = await provider.QuoteAsync(Request([new ShippingPackage(weight, length, width, height, 1000)]));

        Assert.Empty(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Quote_rejects_provider_tariff_that_is_already_expired()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, """{"token":"cached-token"}""")
            : Json(HttpStatusCode.OK, Rates(now.AddSeconds(-1),
                """{"deliveredType":"D","productType":"CP","productName":"Clásico","price":100}""")));
        var provider = CreateProvider(handler, now);

        Assert.Empty(await provider.QuoteAsync(Request([new ShippingPackage(1000, 10, 10, 10, 1000)])));
    }

    [Theory]
    [InlineData("token")]
    [InlineData("rates")]
    public async Task Quote_returns_no_options_when_provider_success_payload_is_invalid(string invalidOperation)
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) =>
        {
            var isToken = request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal);
            if ((isToken && invalidOperation == "token") || (!isToken && invalidOperation == "rates"))
                return Json(HttpStatusCode.OK, "not-json");
            return isToken
                ? Json(HttpStatusCode.OK, """{"token":"cached-token"}""")
                : Json(HttpStatusCode.OK, Rates(now.AddMinutes(10),
                    """{"deliveredType":"D","productType":"CP","productName":"Clásico","price":100}"""));
        });
        var provider = CreateProvider(handler, now);

        var result = await provider.QuoteAsync(Request([new ShippingPackage(1000, 10, 10, 10, 1000)]));

        Assert.Empty(result);
    }

    [Fact]
    public void Enabled_options_require_credentials_and_valid_ranges()
    {
        var options = new CorreoArgentinoOptions
        {
            Enabled = true,
            Environment = "invalid",
            TimeoutSeconds = 0,
            QuoteLifetimeMinutes = 0,
            EstimatedDeliveryMinDays = 5,
            EstimatedDeliveryMaxDays = 2
        };

        var failures = options.Validate();

        Assert.Contains(failures, failure => failure.Contains("Environment", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ApiUsername", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("ApiPassword", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("CustomerId", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("TimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("QuoteLifetimeMinutes", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("delivery days", StringComparison.Ordinal));
    }

    private static CorreoArgentinoShippingProvider CreateProvider(RecordingHandler handler, DateTimeOffset now) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://apitest.correoargentino.com.ar/micorreo/v1/") },
            new CorreoArgentinoOptions
            {
                Enabled = true,
                Environment = "Qa",
                ApiUsername = "api-user",
                ApiPassword = "api-password",
                CustomerId = "customer-id",
                QuoteLifetimeMinutes = 15,
                EstimatedDeliveryMinDays = 2,
                EstimatedDeliveryMaxDays = 5
            },
            new CorreoArgentinoTokenCache(),
            new FixedTimeProvider(now),
            NullLogger<CorreoArgentinoShippingProvider>.Instance);

    private static ShippingQuoteRequest Request(IReadOnlyList<ShippingPackage> packages) =>
        new(Address("2000"), Address("4400"), packages, ShippingDeliveryType.HomeDelivery);

    private static ShippingAddress Address(string postalCode) =>
        new(postalCode, "Santa Fe", "Rosario", "Catamarca", "2730", null, null, null);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static string Rates(DateTimeOffset validTo, string rates) =>
        $$"""
          {
            "customerId": "customer-id",
            "validTo": "{{validTo:O}}",
            "rates": [{{rates}}]
          }
          """;

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];
        public int TokenCalls { get; private set; }
        public int RateCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isRates = request.RequestUri!.AbsolutePath.EndsWith("/rates", StringComparison.Ordinal);
            if (isRates) RateCalls++;
            else if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)) TokenCalls++;

            Requests.Add(new RequestSnapshot(
                request.RequestUri.AbsolutePath,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request, isRates ? RateCalls : 0);
        }
    }

    private sealed record RequestSnapshot(string Path, string? AuthorizationScheme, string? AuthorizationValue, string? Body);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
