using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Data;
using GymShop.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace GymShop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, bool useMigrationConnection = false)
    {
        var connectionString = GetPostgresConnectionString(configuration, environment, useMigrationConnection);

        services.AddDbContext<GymShopDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<GymShopDbContext>());
        services.AddScoped<ITransactionManager, EfTransactionManager>();
        var billingOptions = configuration.GetSection(BillingOptions.SectionName).Get<BillingOptions>() ?? new BillingOptions();
        var billingFailures = billingOptions.Validate();
        if (billingFailures.Count > 0)
            throw new InvalidOperationException(string.Join(' ', billingFailures));
        services.AddSingleton<IBillingProfile>(billingOptions);
        services.AddSingleton<IReceiptPdfRenderer, InternalReceiptPdfRenderer>();
        services.AddSingleton<IFiscalInvoicePdfRenderer, FiscalInvoicePdfRenderer>();
        var arcaOptions = configuration.GetSection(ArcaOptions.SectionName).Get<ArcaOptions>() ?? new ArcaOptions();
        var arcaFailures = arcaOptions.Validate(billingOptions.ArcaEnabled);
        if (arcaFailures.Count > 0)
            throw new InvalidOperationException(string.Join(' ', arcaFailures));
        services.AddSingleton(arcaOptions);
        services.AddSingleton<ArcaAccessTicketCache>();
        services.AddHttpClient<IArcaElectronicInvoiceGateway, ArcaElectronicInvoiceGateway>(client =>
            client.Timeout = TimeSpan.FromSeconds(arcaOptions.TimeoutSeconds));

        var shippingOptions = configuration.GetSection(ShippingOptions.SectionName).Get<ShippingOptions>() ?? new ShippingOptions();
        if (shippingOptions.HomeDeliveryCost < 0)
            throw new InvalidOperationException("Shipping:HomeDeliveryCost cannot be negative.");
        if (shippingOptions.QuoteLifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Shipping:QuoteLifetimeMinutes must be between 1 and 1440.");
        if (shippingOptions.EstimatedDeliveryMinDays < 0 || shippingOptions.EstimatedDeliveryMaxDays < shippingOptions.EstimatedDeliveryMinDays)
            throw new InvalidOperationException("Shipping estimated delivery days are invalid.");
        services.AddSingleton<IShippingSettings>(shippingOptions);
        if (shippingOptions.OwnFleetEnabled)
            services.AddSingleton<IShippingProvider, OwnFleetShippingProvider>();

        var ocaOptions = configuration.GetSection(OcaOptions.SectionName).Get<OcaOptions>() ?? new OcaOptions();
        var ocaFailures = ocaOptions.Validate();
        if (ocaFailures.Count > 0)
            throw new InvalidOperationException(string.Join(' ', ocaFailures));
        services.AddSingleton(ocaOptions);
        if (ocaOptions.Enabled)
        {
            services.AddHttpClient<OcaShippingProvider>(client =>
                client.Timeout = TimeSpan.FromSeconds(ocaOptions.TimeoutSeconds));
            services.AddTransient<IShippingProvider>(provider => provider.GetRequiredService<OcaShippingProvider>());
        }

        var correoArgentinoOptions = configuration.GetSection(CorreoArgentinoOptions.SectionName)
            .Get<CorreoArgentinoOptions>() ?? new CorreoArgentinoOptions();
        var correoArgentinoFailures = correoArgentinoOptions.Validate();
        if (correoArgentinoFailures.Count > 0)
            throw new InvalidOperationException(string.Join(' ', correoArgentinoFailures));
        services.AddSingleton(correoArgentinoOptions);
        services.AddSingleton<CorreoArgentinoTokenCache>();
        if (correoArgentinoOptions.Enabled)
        {
            services.AddHttpClient<CorreoArgentinoShippingProvider>(client =>
            {
                client.BaseAddress = correoArgentinoOptions.BaseAddress;
                client.Timeout = TimeSpan.FromSeconds(correoArgentinoOptions.TimeoutSeconds);
            });
            services.AddTransient<IShippingProvider>(provider => provider.GetRequiredService<CorreoArgentinoShippingProvider>());
        }
        services.AddOptions<ProductImageStorageOptions>()
            .Configure(options => options.BucketName = configuration["PRODUCT_IMAGE_BUCKET"] ?? string.Empty);
        services.AddSingleton<IProductImageStorage>(provider =>
            ActivatorUtilities.CreateInstance<NeonProductImageStorage>(provider, configuration));
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IMfaService, MfaService>();
        services.AddSingleton<IValidateOptions<MfaOptions>, MfaOptionsValidator>();
        services.AddOptions<MfaOptions>()
            .Bind(configuration.GetSection(MfaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>>(new EmailOptionsValidator(environment.EnvironmentName));
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Mock";
        if (environment.IsDevelopment() && string.Equals(emailProvider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IVerificationEmailSender, MockVerificationEmailSender>();
            services.AddScoped<IPasswordResetEmailSender, MockPasswordResetEmailSender>();
            services.AddScoped<ITransactionalEmailSender, MockTransactionalEmailSender>();
        }
        else
        {
            services.AddHttpClient<ResendEmailSender>((provider, client) =>
            {
                var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
                client.BaseAddress = new Uri("https://api.resend.com/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", email.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<IVerificationEmailSender>(provider => provider.GetRequiredService<ResendEmailSender>());
            services.AddScoped<IPasswordResetEmailSender>(provider => provider.GetRequiredService<ResendEmailSender>());
            services.AddScoped<ITransactionalEmailSender>(provider => provider.GetRequiredService<ResendEmailSender>());
        }
        services.AddScoped<TransactionalNotificationProcessor>();
        services.AddHostedService<TransactionalNotificationWorker>();
        services.AddHostedService<GuestOrderExpirationWorker>();
        services.AddScoped<IExternalIdentityVerifier>(_ => new GoogleIdentityVerifier(configuration));
        var bankTransferOptions = configuration.GetSection(BankTransferOptions.SectionName).Get<BankTransferOptions>() ?? new BankTransferOptions();
        if (bankTransferOptions.PendingOrderLifetimeHours is < 1 or > 168)
            throw new InvalidOperationException("BankTransfer:PendingOrderLifetimeHours must be between 1 and 168.");
        services.Configure<BankTransferOptions>(configuration.GetSection(BankTransferOptions.SectionName));
        services.AddSingleton<IBankTransferSettings>(bankTransferOptions);
        services.AddScoped<IPaymentGateway, BankTransferPaymentGateway>();
        if (environment.IsDevelopment()) services.AddScoped<IPaymentGateway, MockPaymentGateway>();
        services.AddHttpClient<IPaymentGateway, MercadoPagoPaymentGateway>(client =>
        {
            client.BaseAddress = new Uri("https://api.mercadopago.com/");
        });

        return services;
    }

    private static string GetPostgresConnectionString(IConfiguration configuration, IHostEnvironment environment, bool useMigrationConnection)
    {
        var migrationValue = configuration["DATABASE_MIGRATION_URL"] ?? configuration.GetConnectionString("Migration");
        if (useMigrationConnection && string.IsNullOrWhiteSpace(migrationValue))
            throw new InvalidOperationException("Configure DATABASE_MIGRATION_URL with the direct, limited migration-role connection.");

        var configuredValue = useMigrationConnection
            ? migrationValue
            : configuration["DATABASE_URL"] ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new InvalidOperationException(
                "Configure DATABASE_URL or ConnectionStrings:DefaultConnection.");
        }

        configuredValue = configuredValue.Trim();
        if (configuredValue.Length >= 2
            && ((configuredValue[0] == '"' && configuredValue[^1] == '"')
                || (configuredValue[0] == '\'' && configuredValue[^1] == '\'')))
        {
            configuredValue = configuredValue[1..^1];
        }

        if (!Uri.TryCreate(configuredValue, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
        {
            var builder = new NpgsqlConnectionStringBuilder(configuredValue);
            if (environment.IsProduction())
            {
                builder.SslMode = SslMode.Require;
                builder.ChannelBinding = ChannelBinding.Require;
            }
            return builder.ConnectionString;
        }
        }

        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2)
        {
            throw new InvalidOperationException("DATABASE_URL does not contain valid PostgreSQL credentials.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            SslMode = SslMode.Require,
            ChannelBinding = ChannelBinding.Require
        }.ConnectionString;
    }
}
