using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;

namespace GymShop.Infrastructure.Services;

public sealed class ArcaElectronicInvoiceGateway(
    HttpClient httpClient,
    ArcaOptions options,
    IBillingProfile billingProfile,
    ArcaAccessTicketCache ticketCache,
    TimeProvider timeProvider) : IArcaElectronicInvoiceGateway
{
    private const string WsfeNamespace = "http://ar.gov.afip.dif.FEV1/";
    private const int InvoiceCType = 11;
    private const int CreditNoteCType = 13;

    public async Task<ArcaConnectionStatus> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (!options.IsConfigured)
            return Failure(false, false, "arca_configuration_incomplete", "Faltan el certificado o la clave privada de homologacion.", checkedAt);

        var wsfeReachable = false;
        try
        {
            wsfeReachable = await CheckWsfeHealthAsync(cancellationToken);
            if (!wsfeReachable)
                return Failure(false, false, "arca_wsfe_unavailable", "ARCA respondio, pero alguno de los servicios de WSFE no esta disponible.", checkedAt);

            var ticket = await ticketCache.GetOrCreateAsync(CreateAccessTicketAsync, timeProvider, cancellationToken);
            var pointsOfSale = await GetPointsOfSaleAsync(ticket, cancellationToken);
            return new ArcaConnectionStatus(
                ArcaOptions.HomologationEnvironment,
                true,
                true,
                true,
                pointsOfSale,
                null,
                pointsOfSale.Count == 0
                    ? "La autenticacion funciono, pero ARCA no devolvio puntos de venta habilitados para WSFE."
                    : "Conexion de homologacion validada correctamente.",
                checkedAt);
        }
        catch (ArcaGatewayException exception)
        {
            return Failure(exception.WsaaAuthenticated, wsfeReachable, exception.Code, exception.Message, checkedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(false, wsfeReachable, "arca_timeout", "ARCA no respondio dentro del tiempo configurado.", checkedAt);
        }
        catch (HttpRequestException)
        {
            return Failure(false, wsfeReachable, "arca_network_error", "No se pudo establecer comunicacion con ARCA.", checkedAt);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            return Failure(false, wsfeReachable, "arca_certificate_invalid", "El certificado de homologacion o su clave privada no son validos.", checkedAt);
        }
    }

    public async Task<ArcaInvoiceSequence> GetNextHomologationInvoiceSequenceAsync(
        CancellationToken cancellationToken = default)
        => await GetNextHomologationSequenceAsync(InvoiceCType, cancellationToken);

    public async Task<ArcaInvoiceSequence> GetNextHomologationCreditNoteSequenceAsync(
        CancellationToken cancellationToken = default)
        => await GetNextHomologationSequenceAsync(CreditNoteCType, cancellationToken);

    public async Task<ArcaAuthorizedDocument?> GetAuthorizedHomologationDocumentAsync(
        int pointOfSale,
        int documentType,
        long documentNumber,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (pointOfSale != options.HomologationPointOfSale || documentType is not (InvoiceCType or CreditNoteCType) || documentNumber < 1)
            throw new ArgumentException("The ARCA homologation document query is invalid.");

        var ticket = await ticketCache.GetOrCreateAsync(CreateAccessTicketAsync, timeProvider, cancellationToken);
        var body = new XElement(XName.Get("FECompConsultar", WsfeNamespace),
            CreateAuth(ticket),
            new XElement(XName.Get("FeCompConsReq", WsfeNamespace),
                new XElement(XName.Get("CbteTipo", WsfeNamespace), documentType),
                new XElement(XName.Get("CbteNro", WsfeNamespace), documentNumber),
                new XElement(XName.Get("PtoVta", WsfeNamespace), pointOfSale)));
        var response = await SendSoapAsync(options.WsfeAddress, body, "FECompConsultar", cancellationToken);
        var errors = ReadProviderIssues(response, "Err");
        if (errors.Any(error => error.Code == "602")) return null;
        if (errors.Count > 0)
            throw new HttpRequestException("ARCA rechazo la consulta de reconciliacion del comprobante.");

        var result = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "ResultGet");
        if (result is null)
            throw new HttpRequestException("ARCA no devolvio un resultado para la consulta de reconciliacion.");
        var resultCode = ChildValue(result, "Resultado");
        var cae = ChildValue(result, "CodAutorizacion");
        var expirationValue = ChildValue(result, "FchVto");
        var returnedPointOfSale = ParseInt(ChildValue(result, "PtoVta"));
        var returnedType = ParseInt(ChildValue(result, "CbteTipo"));
        var returnedNumber = ParseLong(ChildValue(result, "CbteDesde"));
        var returnedLastNumber = ParseLong(ChildValue(result, "CbteHasta"));
        var total = ParseDecimal(ChildValue(result, "ImpTotal"));
        if (!string.Equals(resultCode, "A", StringComparison.OrdinalIgnoreCase) ||
            cae?.Length != 14 ||
            !DateOnly.TryParseExact(expirationValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration) ||
            returnedPointOfSale != pointOfSale || returnedType != documentType ||
            returnedNumber != documentNumber || returnedLastNumber != documentNumber || total is null)
            throw new HttpRequestException("ARCA devolvio datos incompletos o inconsistentes para la consulta de reconciliacion.");

        return new ArcaAuthorizedDocument(pointOfSale, documentType, documentNumber, total.Value, cae, expiration);
    }

    private async Task<ArcaInvoiceSequence> GetNextHomologationSequenceAsync(
        int documentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var ticket = await ticketCache.GetOrCreateAsync(CreateAccessTicketAsync, timeProvider, cancellationToken);
        var body = new XElement(XName.Get("FECompUltimoAutorizado", WsfeNamespace),
            CreateAuth(ticket),
            new XElement(XName.Get("PtoVta", WsfeNamespace), options.HomologationPointOfSale),
            new XElement(XName.Get("CbteTipo", WsfeNamespace), documentType));
        var response = await SendSoapAsync(options.WsfeAddress, body, "FECompUltimoAutorizado", cancellationToken);
        var errors = ReadProviderIssues(response, "Err");
        if (errors.Count > 0 && errors.Any(error => error.Code != "602"))
            ThrowBusinessError(errors[0], "WSFE rechazo la consulta del ultimo comprobante.");

        var lastNumber = response.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "CbteNro")?.Value;
        var parsedLastNumber = long.TryParse(lastNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
        return new ArcaInvoiceSequence(options.HomologationPointOfSale, documentType, checked(parsedLastNumber + 1));
    }

    public async Task<ArcaInvoiceAuthorization> AuthorizeHomologationInvoiceAsync(
        ArcaInvoiceAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (request.PointOfSale != options.HomologationPointOfSale || request.InvoiceType != InvoiceCType ||
            request.DocumentNumber < 1 || request.Total <= 0)
            throw new ArgumentException("The ARCA homologation invoice request is invalid.", nameof(request));

        return await AuthorizeAsync(request.PointOfSale, request.InvoiceType, request.DocumentNumber,
            request.IssuedOn, request.Total, null, cancellationToken);
    }

    public async Task<ArcaInvoiceAuthorization> AuthorizeHomologationCreditNoteAsync(
        ArcaCreditNoteAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (request.PointOfSale != options.HomologationPointOfSale || request.CreditNoteType != CreditNoteCType ||
            request.DocumentNumber < 1 || request.Total <= 0 || request.AssociatedInvoiceType != InvoiceCType ||
            request.AssociatedPointOfSale < 1 || request.AssociatedDocumentNumber < 1)
            throw new ArgumentException("The ARCA homologation credit note request is invalid.", nameof(request));

        var associated = new ArcaAssociatedDocument(
            request.AssociatedInvoiceType,
            request.AssociatedPointOfSale,
            request.AssociatedDocumentNumber,
            request.AssociatedIssuedOn);
        return await AuthorizeAsync(request.PointOfSale, request.CreditNoteType, request.DocumentNumber,
            request.IssuedOn, request.Total, associated, cancellationToken);
    }

    private async Task<ArcaInvoiceAuthorization> AuthorizeAsync(
        int pointOfSale,
        int documentType,
        long documentNumber,
        DateOnly issuedOn,
        decimal total,
        ArcaAssociatedDocument? associated,
        CancellationToken cancellationToken)
    {
        var ticket = await ticketCache.GetOrCreateAsync(CreateAccessTicketAsync, timeProvider, cancellationToken);
        var amount = total.ToString("0.00", CultureInfo.InvariantCulture);
        var requestDetail = new XElement(XName.Get("FECAEDetRequest", WsfeNamespace),
            new XElement(XName.Get("Concepto", WsfeNamespace), 1),
            new XElement(XName.Get("DocTipo", WsfeNamespace), 99),
            new XElement(XName.Get("DocNro", WsfeNamespace), 0),
            new XElement(XName.Get("CbteDesde", WsfeNamespace), documentNumber),
            new XElement(XName.Get("CbteHasta", WsfeNamespace), documentNumber),
            new XElement(XName.Get("CbteFch", WsfeNamespace), issuedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new XElement(XName.Get("ImpTotal", WsfeNamespace), amount),
            new XElement(XName.Get("ImpTotConc", WsfeNamespace), "0.00"),
            new XElement(XName.Get("ImpNeto", WsfeNamespace), amount),
            new XElement(XName.Get("ImpOpEx", WsfeNamespace), "0.00"),
            new XElement(XName.Get("ImpTrib", WsfeNamespace), "0.00"),
            new XElement(XName.Get("ImpIVA", WsfeNamespace), "0.00"),
            new XElement(XName.Get("MonId", WsfeNamespace), "PES"),
            new XElement(XName.Get("MonCotiz", WsfeNamespace), "1.00"),
            new XElement(XName.Get("CondicionIVAReceptorId", WsfeNamespace), 5));
        if (associated is not null)
        {
            requestDetail.Add(new XElement(XName.Get("CbtesAsoc", WsfeNamespace),
                new XElement(XName.Get("CbteAsoc", WsfeNamespace),
                    new XElement(XName.Get("Tipo", WsfeNamespace), associated.Type),
                    new XElement(XName.Get("PtoVta", WsfeNamespace), associated.PointOfSale),
                    new XElement(XName.Get("Nro", WsfeNamespace), associated.DocumentNumber),
                    new XElement(XName.Get("Cuit", WsfeNamespace), DigitsOnly(billingProfile.Cuit)),
                    new XElement(XName.Get("CbteFch", WsfeNamespace), associated.IssuedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))));
        }
        var body = new XElement(XName.Get("FECAESolicitar", WsfeNamespace),
            CreateAuth(ticket),
            new XElement(XName.Get("FeCAEReq", WsfeNamespace),
                new XElement(XName.Get("FeCabReq", WsfeNamespace),
                    new XElement(XName.Get("CantReg", WsfeNamespace), 1),
                    new XElement(XName.Get("PtoVta", WsfeNamespace), pointOfSale),
                    new XElement(XName.Get("CbteTipo", WsfeNamespace), documentType)),
                new XElement(XName.Get("FeDetReq", WsfeNamespace),
                    requestDetail)));
        var response = await SendSoapAsync(options.WsfeAddress, body, "FECAESolicitar", cancellationToken);
        var detail = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "FECAEDetResponse");
        var errors = ReadProviderIssues(response, "Err");
        var observations = detail is null ? [] : ReadProviderIssues(detail, "Obs");
        var result = detail?.Elements().FirstOrDefault(x => x.Name.LocalName == "Resultado")?.Value;
        var cae = detail?.Elements().FirstOrDefault(x => x.Name.LocalName == "CAE")?.Value;
        var expirationValue = detail?.Elements().FirstOrDefault(x => x.Name.LocalName == "CAEFchVto")?.Value;

        if (string.Equals(result, "A", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cae) &&
            DateOnly.TryParseExact(expirationValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration))
        {
            return new ArcaInvoiceAuthorization(
                true, pointOfSale, documentType, documentNumber,
                cae, expiration, null, null);
        }

        var rejection = observations.FirstOrDefault() ?? errors.FirstOrDefault();
        return new ArcaInvoiceAuthorization(
            false, pointOfSale, documentType, documentNumber,
            null, null,
            rejection?.Code ?? "arca_rejected",
            SanitizeProviderMessage(rejection?.Message, "ARCA rechazo el comprobante de homologacion."));
    }

    private sealed record ArcaAssociatedDocument(int Type, int PointOfSale, long DocumentNumber, DateOnly IssuedOn);

    private static string? ChildValue(XContainer parent, string name) =>
        parent.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private ArcaConnectionStatus Failure(bool authenticated, bool reachable, string code, string message, DateTime checkedAt) =>
        new(ArcaOptions.HomologationEnvironment, options.IsConfigured, authenticated, reachable, [], code, message, checkedAt);

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
            throw new InvalidOperationException("ARCA homologation is not configured.");
        if (string.IsNullOrWhiteSpace(DigitsOnly(billingProfile.Cuit)))
            throw new InvalidOperationException("The represented CUIT is not configured.");
    }

    private async Task<bool> CheckWsfeHealthAsync(CancellationToken cancellationToken)
    {
        var body = new XElement(XName.Get("FEDummy", WsfeNamespace));
        var response = await SendSoapAsync(options.WsfeAddress, body, "FEDummy", cancellationToken);
        var values = response.Descendants()
            .Where(x => x.Name.LocalName is "AppServer" or "DbServer" or "AuthServer")
            .Select(x => x.Value.Trim())
            .ToList();
        return values.Count == 3 && values.All(x => string.Equals(x, "OK", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ArcaAccessTicket> CreateAccessTicketAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var uniqueId = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var loginTicketRequest = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("loginTicketRequest",
                new XAttribute("version", "1.0"),
                new XElement("header",
                    new XElement("uniqueId", uniqueId),
                    new XElement("generationTime", now.AddMinutes(-5).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)),
                    new XElement("expirationTime", now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))),
                new XElement("service", "wsfe")));

        var signingMaterial = LoadSigningMaterial();
        using var certificate = signingMaterial.Certificate;
        using var privateKey = signingMaterial.PrivateKey;
        var content = new ContentInfo(Encoding.UTF8.GetBytes(loginTicketRequest.ToString(SaveOptions.DisableFormatting)));
        var signedCms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            PrivateKey = privateKey
        };
        try
        {
            signedCms.ComputeSignature(signer, silent: true);
        }
        catch (CryptographicException)
        {
            throw new ArcaGatewayException(
                "arca_certificate_signing_failed",
                "El certificado se cargo, pero el servidor no pudo firmar la solicitud para ARCA.",
                false);
        }
        var cms = Convert.ToBase64String(signedCms.Encode());

        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";
        var envelope = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsaa", wsaa.NamespaceName),
                new XElement(soap + "Header"),
                new XElement(soap + "Body",
                    new XElement(wsaa + "loginCms", new XElement(wsaa + "in0", cms)))));

        var response = await SendXmlAsync(options.WsaaAddress, envelope, "loginCms", cancellationToken);
        ThrowIfSoapFault(response, false);
        var returnValue = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "loginCmsReturn")?.Value;
        if (string.IsNullOrWhiteSpace(returnValue))
            throw new ArcaGatewayException("arca_wsaa_invalid_response", "WSAA devolvio una respuesta sin credenciales.", false);

        XDocument ticketDocument;
        try { ticketDocument = XDocument.Parse(returnValue); }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new ArcaGatewayException("arca_wsaa_invalid_response", "WSAA devolvio credenciales con un formato inesperado.", false);
        }

        var token = ticketDocument.Descendants().FirstOrDefault(x => x.Name.LocalName == "token")?.Value;
        var sign = ticketDocument.Descendants().FirstOrDefault(x => x.Name.LocalName == "sign")?.Value;
        var expiration = ticketDocument.Descendants().FirstOrDefault(x => x.Name.LocalName == "expirationTime")?.Value;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sign) ||
            !DateTimeOffset.TryParse(expiration, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var expiresAt))
            throw new ArcaGatewayException("arca_wsaa_invalid_response", "WSAA no devolvio un ticket de acceso completo.", false);

        return new ArcaAccessTicket(token, sign, expiresAt);
    }

    private (X509Certificate2 Certificate, RSA PrivateKey) LoadSigningMaterial()
    {
        try
        {
            return options.LoadSigningMaterial();
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            throw new ArcaGatewayException(
                "arca_certificate_load_failed",
                "No se pudo cargar el certificado de homologacion o su clave privada.",
                false);
        }
    }

    private async Task<IReadOnlyList<int>> GetPointsOfSaleAsync(ArcaAccessTicket ticket, CancellationToken cancellationToken)
    {
        var body = new XElement(XName.Get("FEParamGetPtosVenta", WsfeNamespace), CreateAuth(ticket));
        var response = await SendSoapAsync(options.WsfeAddress, body, "FEParamGetPtosVenta", cancellationToken);
        var errors = ReadProviderIssues(response, "Err");
        if (errors.Count > 0 && errors.Any(error => error.Code != "602"))
            ThrowBusinessError(errors[0], "WSFE rechazo la consulta de puntos de venta.");
        return response.Descendants()
            .Where(x => x.Name.LocalName == "Nro")
            .Select(x => int.TryParse(x.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .Where(x => x > 0)
            .Distinct()
            .Order()
            .ToList();
    }

    private XElement CreateAuth(ArcaAccessTicket ticket) =>
        new(XName.Get("Auth", WsfeNamespace),
            new XElement(XName.Get("Token", WsfeNamespace), ticket.Token),
            new XElement(XName.Get("Sign", WsfeNamespace), ticket.Sign),
            new XElement(XName.Get("Cuit", WsfeNamespace), DigitsOnly(billingProfile.Cuit)));

    private async Task<XDocument> SendSoapAsync(Uri address, XElement body, string action, CancellationToken cancellationToken)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = new XDocument(
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soap.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ar", WsfeNamespace),
                new XElement(soap + "Header"),
                new XElement(soap + "Body", body)));
        var response = await SendXmlAsync(address, envelope, $"{WsfeNamespace}{action}", cancellationToken);
        ThrowIfSoapFault(response, true);
        return response;
    }

    private async Task<XDocument> SendXmlAsync(Uri address, XDocument document, string soapAction, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, address);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Content = new StringContent(document.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(xml))
            throw new ArcaGatewayException("arca_http_error", "ARCA rechazo la solicitud HTTP.", false);
        try { return XDocument.Parse(xml); }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw new ArcaGatewayException("arca_invalid_response", "ARCA devolvio una respuesta que no es XML valido.", false);
        }
    }

    private static void ThrowIfSoapFault(XDocument response, bool authenticated)
    {
        var fault = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "Fault");
        if (fault is null) return;
        var detail = fault.Descendants().FirstOrDefault(x => x.Name.LocalName is "faultstring" or "message")?.Value;
        throw new ArcaGatewayException(
            authenticated ? "arca_wsfe_fault" : "arca_wsaa_rejected",
            SanitizeProviderMessage(detail, authenticated ? "WSFE rechazo la solicitud." : "WSAA rechazo el certificado o la autorizacion."),
            authenticated);
    }

    private static List<ProviderIssue> ReadProviderIssues(XContainer response, string elementName) =>
        response.Descendants()
            .Where(x => x.Name.LocalName == elementName)
            .Select(x => new ProviderIssue(
                x.Elements().FirstOrDefault(y => y.Name.LocalName == "Code")?.Value,
                x.Elements().FirstOrDefault(y => y.Name.LocalName == "Msg")?.Value))
            .ToList();

    private static void ThrowBusinessError(ProviderIssue issue, string fallback)
    {
        var code = string.IsNullOrWhiteSpace(issue.Code) ? "unknown" : issue.Code;
        throw new ArcaGatewayException(
            $"arca_wsfe_{code}",
            SanitizeProviderMessage(issue.Message, fallback),
            true);
    }

    private static string SanitizeProviderMessage(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private sealed class ArcaGatewayException(string code, string message, bool wsaaAuthenticated) : Exception(message)
    {
        public string Code { get; } = code;
        public bool WsaaAuthenticated { get; } = wsaaAuthenticated;
    }

    private sealed record ProviderIssue(string? Code, string? Message);
}
