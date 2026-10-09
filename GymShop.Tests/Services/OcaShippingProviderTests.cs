using System.Net;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymShop.Tests.Services;

public sealed class OcaShippingProviderTests
{
    [Fact]
    public async Task Quote_maps_packages_and_parses_oca_tariff()
    {
        const string responseXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <DataSet xmlns="#Oca_e_Pak">
              <diffgr:diffgram xmlns:diffgr="urn:schemas-microsoft-com:xml-diffgram-v1">
                <NewDataSet xmlns="">
                  <Table>
                    <Precio>871.9200</Precio>
                    <idTiposervicio>2</idTiposervicio>
                    <Ambito>Regional</Ambito>
                    <PlazoEntrega>1</PlazoEntrega>
                    <Total>871.9200</Total>
                  </Table>
                </NewDataSet>
              </diffgr:diffgram>
            </DataSet>
            """;
        var handler = new RecordingHandler(responseXml);
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var provider = CreateProvider(handler, now);
        var request = new ShippingQuoteRequest(
            Address("S2000ABC"),
            Address("C1000ABC"),
            [new ShippingPackage(10_500, 35, 18, 18, 25_000, 2)],
            ShippingDeliveryType.HomeDelivery);

        var result = Assert.Single(await provider.QuoteAsync(request));

        Assert.Equal(OcaShippingProvider.ProviderCode, result.ProviderCode);
        Assert.Equal("operativa-64665-tipo-2", result.ServiceCode);
        Assert.Equal("OCA e-Pak · Regional", result.ServiceName);
        Assert.Equal(871.92m, result.Price);
        Assert.Equal(now.AddDays(1), result.EstimatedDeliveryFrom);
        Assert.Equal(now.AddMinutes(15), result.ExpiresAt);

        var form = ParseForm(handler.RequestBody!);
        Assert.Equal("30-53625919-4", form["Cuit"]);
        Assert.Equal("64665", form["Operativa"]);
        Assert.Equal("21", form["PesoTotal"]);
        Assert.Equal("0.02268", form["VolumenTotal"]);
        Assert.Equal("2000", form["CodigoPostalOrigen"]);
        Assert.Equal("1000", form["CodigoPostalDestino"]);
        Assert.Equal("2", form["CantidadPaquetes"]);
        Assert.Equal("25000", form["ValorDeclarado"]);
    }

    [Fact]
    public async Task Quote_does_not_call_oca_when_postal_code_is_invalid()
    {
        var handler = new RecordingHandler("<DataSet />");
        var provider = CreateProvider(handler, DateTimeOffset.UtcNow);
        var request = new ShippingQuoteRequest(
            Address("invalid"),
            Address("2000"),
            [new ShippingPackage(1000, 10, 10, 10, 1000)],
            ShippingDeliveryType.HomeDelivery);

        Assert.Empty(await provider.QuoteAsync(request));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Enabled_options_require_real_oca_configuration()
    {
        var options = new OcaOptions { Enabled = true, Environment = "invalid" };

        var failures = options.Validate();

        Assert.Contains(failures, failure => failure.Contains("Environment", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Cuit", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Operativa", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quote_logs_sanitized_reason_when_oca_rejects_cuit_or_operativa()
    {
        const string responseXml = """
            <DataSet xmlns="#Oca_e_Pak">
              <NewDataSet xmlns="">
                <Table1><Error>El CUIT o la operativa son inválidos.</Error></Table1>
              </NewDataSet>
            </DataSet>
            """;
        var handler = new RecordingHandler(responseXml);
        var logger = new RecordingLogger();
        var provider = CreateProvider(handler, DateTimeOffset.UtcNow, logger);
        var request = new ShippingQuoteRequest(
            Address("2000"),
            Address("4400"),
            [new ShippingPackage(21_000, 40, 40, 19.99m, 30_000)],
            ShippingDeliveryType.HomeDelivery);

        Assert.Empty(await provider.QuoteAsync(request));
        Assert.Contains("invalid_cuit_or_operativa", logger.Messages.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("El CUIT o la operativa", logger.Messages.Single(), StringComparison.Ordinal);
    }

    private static OcaShippingProvider CreateProvider(
        RecordingHandler handler,
        DateTimeOffset now,
        Microsoft.Extensions.Logging.ILogger<OcaShippingProvider>? logger = null) =>
        new(
            new HttpClient(handler),
            new OcaOptions
            {
                Enabled = true,
                Environment = "Qa",
                Cuit = "30-53625919-4",
                Operativa = 64665,
                QuoteLifetimeMinutes = 15
            },
            new FixedTimeProvider(now),
            logger ?? NullLogger<OcaShippingProvider>.Instance);

    private static ShippingAddress Address(string postalCode) =>
        new(postalCode, "Santa Fe", "Rosario", "Catamarca", "2730", null, null, null);

    private static Dictionary<string, string> ParseForm(string value) => value.Split('&')
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(
            pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
            pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
            StringComparer.Ordinal);

    private sealed class RecordingHandler(string responseXml) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml)
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<OcaShippingProvider>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
