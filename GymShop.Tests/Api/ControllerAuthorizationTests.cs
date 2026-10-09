using System.Reflection;
using GymShop.Api.Controllers;
using GymShop.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace GymShop.Tests.Api;

public class ControllerAuthorizationTests
{
    [Fact]
    public void Product_creation_is_restricted_to_admins()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.Create));
        var authorize = method?.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin,SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("User", SplitRoles(authorize.Roles));
    }

    [Fact]
    public void User_administration_is_restricted_to_superadmin()
    {
        var authorize = typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("Admin", SplitRoles(authorize.Roles));
    }

    [Fact]
    public void Audit_query_is_restricted_to_superadmin()
    {
        var authorize = typeof(AuditController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("Admin", SplitRoles(authorize.Roles));
        Assert.Single(typeof(AuditController).GetMethods(), x => x.GetCustomAttribute<HttpGetAttribute>() is not null);
    }

    [Fact]
    public void MercadoPago_webhook_signature_validator_rejects_invalid_signature()
    {
        var isValid = MercadoPagoWebhookSignatureValidator.IsValid(
            "ts=123456,v1=invalid-signature",
            "request-id-1",
            "payment-id-1",
            "webhook-secret",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5),
            out _, out _);

        Assert.False(isValid);
    }

    [Fact]
    public void MercadoPago_webhook_signature_validator_accepts_fresh_and_rejects_stale_signature()
    {
        var now = DateTimeOffset.UtcNow;
        var freshTimestamp = now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var staleTimestamp = now.AddMinutes(-6).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(Validate(Sign("payment", "request-fresh", freshTimestamp), "request-fresh", now, out var requestHash));
        Assert.NotNull(requestHash);
        Assert.False(Validate(Sign("payment", "request-stale", staleTimestamp), "request-stale", now, out _));
    }

    private static bool Validate(string signature, string requestId, DateTimeOffset now, out string? requestHash) =>
        MercadoPagoWebhookSignatureValidator.IsValid(signature, requestId, "payment", "webhook-secret", now,
            TimeSpan.FromMinutes(5), out requestHash, out _);

    private static string Sign(string dataId, string requestId, string timestamp)
    {
        var manifest = $"id:{dataId};request-id:{requestId};ts:{timestamp};";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-secret"), Encoding.UTF8.GetBytes(manifest));
        return $"ts={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Theory]
    [InlineData(false, "token", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "token", true)]
    public void MercadoPago_availability_requires_enabled_integration_and_credentials(bool enabled, string token, bool expected)
    {
        Assert.Equal(expected, PaymentsController.IsMercadoPagoAvailable(new MercadoPagoOptions { Enabled = enabled, AccessToken = token }));
    }

    [Fact]
    public void Payment_creation_exposes_only_explicit_order_route()
    {
        var postRoutes = typeof(PaymentsController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(attribute => attribute.Template)
            .ToList();

        Assert.Contains("/api/orders/{orderId:int}/payments", postRoutes);
        Assert.DoesNotContain(postRoutes, route => route?.Contains("current", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string[] SplitRoles(string? roles)
    {
        return roles?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    }

    [Fact]
    public void Category_administration_is_restricted_to_admins()
    {
        var methods = new[] { nameof(CategoriesController.GetAdmin), nameof(CategoriesController.GetById), nameof(CategoriesController.Create), nameof(CategoriesController.Update), nameof(CategoriesController.UpdateStatus) };
        foreach (var name in methods) Assert.Equal("Admin,SuperAdmin", typeof(CategoriesController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(CategoriesController).GetMethod(nameof(CategoriesController.GetAll))!.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Order_administration_is_restricted_to_admins()
    {
        var methods = new[] { nameof(OrdersController.GetAll), nameof(OrdersController.GetHistory), nameof(OrdersController.UpdateStatus), nameof(OrdersController.Cancel), nameof(OrdersController.ExpirePending) };
        foreach (var name in methods)
        {
            var authorize = typeof(OrdersController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal("Admin,SuperAdmin", authorize!.Roles);
            Assert.DoesNotContain("User", SplitRoles(authorize.Roles));
        }

        Assert.NotNull(typeof(OrdersController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Dashboard_is_restricted_to_admin_and_superadmin()
    {
        var authorize = typeof(DashboardController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin,SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("User", SplitRoles(authorize.Roles));
        Assert.Single(typeof(DashboardController).GetMethods(), method => method.GetCustomAttribute<HttpGetAttribute>() is not null);
    }

    [Fact]
    public void Billing_profile_is_restricted_to_admin_and_superadmin()
    {
        var authorize = typeof(BillingController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin,SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("User", SplitRoles(authorize.Roles));
    }

    [Fact]
    public void Stock_management_is_restricted_to_admin_and_superadmin()
    {
        var authorize = typeof(StockController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin,SuperAdmin", authorize!.Roles);
        Assert.DoesNotContain("User", SplitRoles(authorize.Roles));
    }
}
