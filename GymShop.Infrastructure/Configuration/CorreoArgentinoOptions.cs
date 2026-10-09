namespace GymShop.Infrastructure.Configuration;

public sealed class CorreoArgentinoOptions
{
    public const string SectionName = "CorreoArgentino";

    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Qa";
    public string ApiUsername { get; set; } = string.Empty;
    public string ApiPassword { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
    public int QuoteLifetimeMinutes { get; set; } = 15;
    public int EstimatedDeliveryMinDays { get; set; } = 2;
    public int EstimatedDeliveryMaxDays { get; set; } = 5;

    public Uri BaseAddress => string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase)
        ? new Uri("https://api.correoargentino.com.ar/micorreo/v1/")
        : new Uri("https://apitest.correoargentino.com.ar/micorreo/v1/");

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled) return [];

        var failures = new List<string>();
        if (!string.Equals(Environment, "Qa", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase))
            failures.Add("CorreoArgentino:Environment must be Qa or Production.");
        if (string.IsNullOrWhiteSpace(ApiUsername))
            failures.Add("CorreoArgentino:ApiUsername is required when the provider is enabled.");
        if (string.IsNullOrWhiteSpace(ApiPassword))
            failures.Add("CorreoArgentino:ApiPassword is required when the provider is enabled.");
        if (string.IsNullOrWhiteSpace(CustomerId) || CustomerId.Length > 50)
            failures.Add("CorreoArgentino:CustomerId is required and must not exceed 50 characters.");
        if (TimeoutSeconds is < 1 or > 60)
            failures.Add("CorreoArgentino:TimeoutSeconds must be between 1 and 60.");
        if (QuoteLifetimeMinutes is < 1 or > 1440)
            failures.Add("CorreoArgentino:QuoteLifetimeMinutes must be between 1 and 1440.");
        if (EstimatedDeliveryMinDays < 0 || EstimatedDeliveryMaxDays < EstimatedDeliveryMinDays)
            failures.Add("CorreoArgentino estimated delivery days are invalid.");
        return failures;
    }
}
