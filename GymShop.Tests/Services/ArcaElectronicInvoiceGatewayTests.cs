using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using GymShop.Application.Abstractions;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;

namespace GymShop.Tests.Services;

public sealed class ArcaElectronicInvoiceGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Check_connection_authenticates_and_returns_wsfe_points_of_sale()
    {
        var options = CreateOptions();
        var handler = new RecordingHandler((request, call) =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.RequestUri == options.WsaaAddress)
            {
                Assert.Contains("loginCms", body);
                return Xml(WsaaResponse(Now.AddHours(12)));
            }

            if (body.Contains("FEDummy", StringComparison.Ordinal))
                return Xml(DummyResponse());

            Assert.Contains("FEParamGetPtosVenta", body);
            Assert.Contains("30536259194", body);
            Assert.Contains("token-de-prueba", body);
            return Xml(PointsOfSaleResponse(4, 12));
        });
        var gateway = CreateGateway(options, handler);

        var first = await gateway.CheckConnectionAsync();
        var second = await gateway.CheckConnectionAsync();

        Assert.True(first.ConfigurationReady);
        Assert.True(first.WsfeReachable);
        Assert.True(first.WsaaAuthenticated);
        Assert.Equal([4, 12], first.PointsOfSale);
        Assert.Null(first.ErrorCode);
        Assert.Equal(5, handler.Calls);
        Assert.Equal(first.PointsOfSale, second.PointsOfSale);
    }

    [Fact]
    public async Task Check_connection_reports_incomplete_configuration_without_http_requests()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP should not be called"));
        var gateway = CreateGateway(new ArcaOptions(), handler);

        var result = await gateway.CheckConnectionAsync();

        Assert.False(result.ConfigurationReady);
        Assert.False(result.WsaaAuthenticated);
        Assert.False(result.WsfeReachable);
        Assert.Equal("arca_configuration_incomplete", result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Check_connection_does_not_expose_wsaa_fault_details_beyond_a_bounded_message()
    {
        var options = CreateOptions();
        var handler = new RecordingHandler((request, _) =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return request.RequestUri == options.WsaaAddress
                ? Xml("""
                    <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
                      <soap:Body><soap:Fault><faultstring>certificado no autorizado</faultstring></soap:Fault></soap:Body>
                    </soap:Envelope>
                    """)
                : Xml(DummyResponse());
        });
        var gateway = CreateGateway(options, handler);

        var result = await gateway.CheckConnectionAsync();

        Assert.Equal("arca_wsaa_rejected", result.ErrorCode);
        Assert.False(result.WsaaAuthenticated);
        Assert.True(result.WsfeReachable);
        Assert.Contains("certificado no autorizado", result.Message);
    }

    [Fact]
    public async Task Check_connection_treats_no_points_error_as_authenticated_without_points()
    {
        var options = CreateOptions();
        var handler = new RecordingHandler((request, _) =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.RequestUri == options.WsaaAddress) return Xml(WsaaResponse(Now.AddHours(12)));
            return Xml(body.Contains("FEDummy", StringComparison.Ordinal)
                ? DummyResponse()
                : ErrorResponse("602", "Sin Resultados: - Metodo FEParamGetPtosVenta"));
        });

        var result = await CreateGateway(options, handler).CheckConnectionAsync();

        Assert.True(result.WsaaAuthenticated);
        Assert.True(result.WsfeReachable);
        Assert.Empty(result.PointsOfSale);
        Assert.Null(result.ErrorCode);
        Assert.Contains("no devolvio puntos", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sequence_uses_configured_point_and_invoice_c_then_returns_next_number()
    {
        var options = CreateOptions();
        options.HomologationPointOfSale = 7;
        string? wsfeBody = null;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri == options.WsaaAddress) return Xml(WsaaResponse(Now.AddHours(12)));
            wsfeBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Xml(LastAuthorizedResponse(41));
        });

        var result = await CreateGateway(options, handler).GetNextHomologationInvoiceSequenceAsync();

        Assert.Equal(new ArcaInvoiceSequence(7, 11, 42), result);
        Assert.Equal("7", ElementValue(wsfeBody, "PtoVta"));
        Assert.Equal("11", ElementValue(wsfeBody, "CbteTipo"));
    }

    [Fact]
    public async Task Sequence_starts_at_one_when_arca_reports_no_previous_results()
    {
        var options = CreateOptions();
        var handler = new RecordingHandler((request, _) => request.RequestUri == options.WsaaAddress
            ? Xml(WsaaResponse(Now.AddHours(12)))
            : Xml(ErrorResponse("602", "Sin Resultados")));

        var result = await CreateGateway(options, handler).GetNextHomologationInvoiceSequenceAsync();

        Assert.Equal(1, result.DocumentNumber);
    }

    [Fact]
    public async Task Authorization_sends_consumer_final_invoice_c_and_parses_cae()
    {
        var options = CreateOptions();
        string? wsfeBody = null;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri == options.WsaaAddress) return Xml(WsaaResponse(Now.AddHours(12)));
            wsfeBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Xml(AuthorizationResponse("A", "74123456789012", "20261013"));
        });
        var request = new ArcaInvoiceAuthorizationRequest(1, 11, 3, new DateOnly(2026, 10, 3), 35000m);

        var result = await CreateGateway(options, handler).AuthorizeHomologationInvoiceAsync(request);

        Assert.True(result.Authorized);
        Assert.Equal("74123456789012", result.Cae);
        Assert.Equal(new DateOnly(2026, 10, 13), result.CaeExpiresOn);
        Assert.Equal("99", ElementValue(wsfeBody, "DocTipo"));
        Assert.Equal("35000.00", ElementValue(wsfeBody, "ImpTotal"));
        Assert.Equal("5", ElementValue(wsfeBody, "CondicionIVAReceptorId"));
    }

    [Fact]
    public async Task Authorization_returns_a_bounded_rejection_from_observations()
    {
        var options = CreateOptions();
        var handler = new RecordingHandler((request, _) => request.RequestUri == options.WsaaAddress
            ? Xml(WsaaResponse(Now.AddHours(12)))
            : Xml(AuthorizationResponse("R", null, null, "10016", "La fecha informada no es valida")));

        var result = await CreateGateway(options, handler).AuthorizeHomologationInvoiceAsync(
            new ArcaInvoiceAuthorizationRequest(1, 11, 1, new DateOnly(2026, 10, 3), 100m));

        Assert.False(result.Authorized);
        Assert.Equal("10016", result.RejectionCode);
        Assert.Equal("La fecha informada no es valida", result.RejectionReason);
    }

    [Fact]
    public async Task Document_query_sends_fiscal_identity_and_recovers_authorization()
    {
        var options = CreateOptions();
        options.HomologationPointOfSale = 4;
        string? wsfeBody = null;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri == options.WsaaAddress) return Xml(WsaaResponse(Now.AddHours(12)));
            wsfeBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Xml(DocumentQueryResponse(4, 11, 27, 35000m, "74123456789016", "20261015"));
        });

        var result = await CreateGateway(options, handler).GetAuthorizedHomologationDocumentAsync(4, 11, 27);

        Assert.NotNull(result);
        Assert.Equal(27, result.DocumentNumber);
        Assert.Equal(35000m, result.Total);
        Assert.Equal("74123456789016", result.Cae);
        Assert.Equal(new DateOnly(2026, 10, 15), result.CaeExpiresOn);
        Assert.Equal("11", ElementValue(wsfeBody, "CbteTipo"));
        Assert.Equal("27", ElementValue(wsfeBody, "CbteNro"));
        Assert.Equal("4", ElementValue(wsfeBody, "PtoVta"));
    }

    [Fact]
    public async Task Document_query_returns_null_when_arca_reports_no_results()
    {
        var options = CreateOptions();
        options.HomologationPointOfSale = 4;
        var handler = new RecordingHandler((request, _) => request.RequestUri == options.WsaaAddress
            ? Xml(WsaaResponse(Now.AddHours(12)))
            : Xml(ErrorResponse("602", "Sin Resultados")));

        var result = await CreateGateway(options, handler).GetAuthorizedHomologationDocumentAsync(4, 13, 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task Credit_note_uses_type_13_and_associates_the_original_invoice_c()
    {
        var options = CreateOptions();
        string? sequenceBody = null;
        string? authorizationBody = null;
        var handler = new RecordingHandler((request, call) =>
        {
            if (request.RequestUri == options.WsaaAddress) return Xml(WsaaResponse(Now.AddHours(12)));
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("FECompUltimoAutorizado", StringComparison.Ordinal))
            {
                sequenceBody = body;
                return Xml(LastAuthorizedResponse(8));
            }
            authorizationBody = body;
            return Xml(AuthorizationResponse("A", "74123456789015", "20261013"));
        });
        var gateway = CreateGateway(options, handler);

        var sequence = await gateway.GetNextHomologationCreditNoteSequenceAsync();
        var result = await gateway.AuthorizeHomologationCreditNoteAsync(new ArcaCreditNoteAuthorizationRequest(
            sequence.PointOfSale, sequence.InvoiceType, sequence.DocumentNumber, new DateOnly(2026, 10, 5), 35000m,
            11, 1, 4, new DateOnly(2026, 10, 3)));

        Assert.Equal(13, sequence.InvoiceType);
        Assert.Equal(9, sequence.DocumentNumber);
        Assert.Equal("13", ElementValue(sequenceBody, "CbteTipo"));
        Assert.True(result.Authorized);
        Assert.Equal("13", ElementValue(authorizationBody, "CbteTipo"));
        var associated = XDocument.Parse(Assert.IsType<string>(authorizationBody)).Descendants()
            .Single(x => x.Name.LocalName == "CbteAsoc");
        Assert.Equal("11", associated.Elements().Single(x => x.Name.LocalName == "Tipo").Value);
        Assert.Equal("1", associated.Elements().Single(x => x.Name.LocalName == "PtoVta").Value);
        Assert.Equal("4", associated.Elements().Single(x => x.Name.LocalName == "Nro").Value);
        Assert.Equal("30536259194", associated.Elements().Single(x => x.Name.LocalName == "Cuit").Value);
        Assert.Equal("20261003", associated.Elements().Single(x => x.Name.LocalName == "CbteFch").Value);
    }

    private static ArcaElectronicInvoiceGateway CreateGateway(ArcaOptions options, HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            options,
            new TestBillingProfile(),
            new ArcaAccessTicketCache(),
            new FixedTimeProvider(Now));

    private static ArcaOptions CreateOptions()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=GymShop ARCA Homologation", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        return new ArcaOptions
        {
            CertificatePemBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(certificate.ExportCertificatePem())),
            PrivateKeyPemBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()))
        };
    }

    private static HttpResponseMessage Xml(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "text/xml")
    };

    private static string DummyResponse() => """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><FEDummyResponse><FEDummyResult><AppServer>OK</AppServer><DbServer>OK</DbServer><AuthServer>OK</AuthServer></FEDummyResult></FEDummyResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string WsaaResponse(DateTimeOffset expiration) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><loginCmsResponse><loginCmsReturn>&lt;loginTicketResponse&gt;&lt;header&gt;&lt;expirationTime&gt;{expiration:yyyy-MM-dd'T'HH:mm:sszzz}&lt;/expirationTime&gt;&lt;/header&gt;&lt;credentials&gt;&lt;token&gt;token-de-prueba&lt;/token&gt;&lt;sign&gt;firma-de-prueba&lt;/sign&gt;&lt;/credentials&gt;&lt;/loginTicketResponse&gt;</loginCmsReturn></loginCmsResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string PointsOfSaleResponse(params int[] points) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><FEParamGetPtosVentaResponse><FEParamGetPtosVentaResult><ResultGet>{string.Join(string.Empty, points.Select(x => $"<PtoVenta><Nro>{x}</Nro><EmisionTipo>CAE</EmisionTipo></PtoVenta>"))}</ResultGet></FEParamGetPtosVentaResult></FEParamGetPtosVentaResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string ErrorResponse(string code, string message) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><Response><Result><Errors><Err><Code>{code}</Code><Msg>{message}</Msg></Err></Errors></Result></Response></soap:Body>
        </soap:Envelope>
        """;

    private static string LastAuthorizedResponse(long number) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><FECompUltimoAutorizadoResponse><FECompUltimoAutorizadoResult><CbteNro>{number}</CbteNro></FECompUltimoAutorizadoResult></FECompUltimoAutorizadoResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string AuthorizationResponse(
        string result,
        string? cae,
        string? expiration,
        string? observationCode = null,
        string? observationMessage = null) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><FECAESolicitarResponse><FECAESolicitarResult><FeDetResp><FECAEDetResponse>
            <Resultado>{result}</Resultado>
            {(cae is null ? string.Empty : $"<CAE>{cae}</CAE>")}
            {(expiration is null ? string.Empty : $"<CAEFchVto>{expiration}</CAEFchVto>")}
            {(observationCode is null ? string.Empty : $"<Observaciones><Obs><Code>{observationCode}</Code><Msg>{observationMessage}</Msg></Obs></Observaciones>")}
          </FECAEDetResponse></FeDetResp></FECAESolicitarResult></FECAESolicitarResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string DocumentQueryResponse(
        int pointOfSale,
        int documentType,
        long documentNumber,
        decimal total,
        string cae,
        string expiration) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body><FECompConsultarResponse><FECompConsultarResult><ResultGet>
            <CbteDesde>{documentNumber}</CbteDesde><CbteHasta>{documentNumber}</CbteHasta>
            <ImpTotal>{total.ToString("0.00", CultureInfo.InvariantCulture)}</ImpTotal><Resultado>A</Resultado>
            <CodAutorizacion>{cae}</CodAutorizacion><FchVto>{expiration}</FchVto>
            <PtoVta>{pointOfSale}</PtoVta><CbteTipo>{documentType}</CbteTipo>
          </ResultGet></FECompConsultarResult></FECompConsultarResponse></soap:Body>
        </soap:Envelope>
        """;

    private static string? ElementValue(string? xml, string localName) =>
        XDocument.Parse(Assert.IsType<string>(xml)).Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request, Calls));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestBillingProfile : IBillingProfile
    {
        public BillingMode Mode => BillingMode.ElectronicInvoice;
        public SellerTaxCondition TaxCondition => SellerTaxCondition.Monotributo;
        public string BusinessName => "Comercio de prueba";
        public string Cuit => "30-53625919-4";
        public string FiscalAddress => "Catamarca 2730, Rosario";
        public string GrossIncomeNumber => "Exento";
        public DateOnly? ActivityStartDate => new(2020, 1, 1);
        public int? PointOfSale => 4;
        public bool ArcaEnabled => true;
        public bool ElectronicInvoicingReady => true;
    }
}
