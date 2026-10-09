namespace GymShop.Infrastructure.Configuration;

public sealed class OcaOptions
{
    public const string SectionName = "Oca";

    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Qa";
    public string Cuit { get; set; } = string.Empty;
    public int Operativa { get; set; }
    public int QuoteLifetimeMinutes { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;

    public Uri QuoteEndpoint => string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase)
        ? new Uri("https://webservice.oca.com.ar/ePak_tracking/Oep_TrackEPak.asmx/Tarifar_Envio_Corporativo")
        : new Uri("https://integraciones.ocadev.com.ar/epak_tracking_test/Oep_TrackEPak.asmx/Tarifar_Envio_Corporativo");

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled) return [];

        var failures = new List<string>();
        if (!string.Equals(Environment, "Qa", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase))
            failures.Add("Oca:Environment must be Qa or Production.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Cuit, @"^\d{2}-\d{8}-\d$"))
            failures.Add("Oca:Cuit must use the ##-########-# format.");
        if (Operativa <= 0)
            failures.Add("Oca:Operativa must be greater than zero.");
        if (QuoteLifetimeMinutes is < 1 or > 1440)
            failures.Add("Oca:QuoteLifetimeMinutes must be between 1 and 1440.");
        if (TimeoutSeconds is < 1 or > 60)
            failures.Add("Oca:TimeoutSeconds must be between 1 and 60.");
        return failures;
    }
}
