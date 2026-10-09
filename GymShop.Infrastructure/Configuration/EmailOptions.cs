using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Mock";
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "GymShop";
    public string PublicAppUrl { get; set; } = string.Empty;
    public bool TransactionalNotificationsEnabled { get; set; } = true;
    public bool ExposeDevelopmentCodes { get; set; }
}

public sealed class EmailOptionsValidator(string environmentName) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (options.ExposeDevelopmentCodes && !isDevelopment)
            return ValidateOptionsResult.Fail("Email:ExposeDevelopmentCodes is only allowed in the Development environment.");

        if (string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            return isDevelopment
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Email:Provider=Mock is only allowed in the Development environment.");
        }

        if (!string.Equals(options.Provider, "Resend", StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail("Email:Provider must be Mock or Resend.");

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey)) failures.Add("Email:ApiKey is required for Resend.");
        if (string.IsNullOrWhiteSpace(options.FromAddress)) failures.Add("Email:FromAddress is required for Resend.");
        if (!string.IsNullOrWhiteSpace(options.PublicAppUrl) &&
            (!Uri.TryCreate(options.PublicAppUrl, UriKind.Absolute, out var publicUrl) || publicUrl.Scheme != Uri.UriSchemeHttps))
            failures.Add("Email:PublicAppUrl must be an absolute HTTPS URL.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
