using System.Net;
using GymShop.Api.Configuration;
using GymShop.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Api;

public class RateLimitingTests
{
    [Fact]
    public void Exceeding_limit_produces_429_problem_details_with_retry_after()
    {
        var limiter = CreateLimiter(loginAccountLimit: 1);
        Assert.True(limiter.Acquire(RateLimitPolicies.LoginAccount, "account-hash").IsAllowed);
        var rejected = limiter.Acquire(RateLimitPolicies.LoginAccount, "account-hash");
        var context = new DefaultHttpContext { TraceIdentifier = "trace-rate-limit" };

        var response = RateLimitResponse.Create(context, rejected);

        Assert.False(rejected.IsAllowed);
        Assert.Equal(StatusCodes.Status429TooManyRequests, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal("trace-rate-limit", problem.Extensions["traceId"]);
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Contains("application/problem+json", response.ContentTypes);
    }

    [Fact]
    public void Changing_ip_does_not_bypass_account_partition()
    {
        var limiter = CreateLimiter(loginAccountLimit: 1);
        var account = GymShopRequestLimiter.HashAccount("CLIENTE@example.com");

        Assert.True(limiter.Acquire(RateLimitPolicies.LoginAccount, account).IsAllowed);
        var afterIpChange = limiter.Acquire(RateLimitPolicies.LoginAccount, account);

        Assert.False(afterIpChange.IsAllowed);
        Assert.Equal(account, GymShopRequestLimiter.HashAccount("cliente@EXAMPLE.com"));
    }

    [Fact]
    public void Different_orders_do_not_share_individual_limit()
    {
        var limiter = CreateLimiter(paymentOrderLimit: 1);

        Assert.True(limiter.Acquire(RateLimitPolicies.PaymentOrder, "order-10").IsAllowed);
        Assert.False(limiter.Acquire(RateLimitPolicies.PaymentOrder, "order-10").IsAllowed);
        Assert.True(limiter.Acquire(RateLimitPolicies.PaymentOrder, "order-11").IsAllowed);
    }

    [Fact]
    public void Different_users_do_not_share_individual_limit()
    {
        var options = ValidOptions();
        options.PaymentUser.PermitLimit = 1;
        var limiter = new GymShopRequestLimiter(Options.Create(options), TimeProvider.System);

        Assert.True(limiter.Acquire(RateLimitPolicies.PaymentUser, "user-1").IsAllowed);
        Assert.False(limiter.Acquire(RateLimitPolicies.PaymentUser, "user-1").IsAllowed);
        Assert.True(limiter.Acquire(RateLimitPolicies.PaymentUser, "user-2").IsAllowed);
    }

    [Fact]
    public void Development_can_explicitly_disable_limits()
    {
        var options = ValidOptions();
        options.Enabled = false;
        var limiter = new GymShopRequestLimiter(Options.Create(options), TimeProvider.System);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.Acquire(RateLimitPolicies.LoginAccount, "same-account").IsAllowed);
        }

        Assert.True(new GymShopRateLimitingOptionsValidator("Development").Validate(null, options).Succeeded);
        Assert.False(new GymShopRateLimitingOptionsValidator("Production").Validate(null, options).Succeeded);
    }

    [Fact]
    public async Task Forwarded_for_is_ignored_from_untrusted_sender_and_honored_from_known_proxy()
    {
        var client = IPAddress.Parse("203.0.113.25");
        var proxy = IPAddress.Parse("10.0.0.10");
        var untrusted = IPAddress.Parse("10.0.0.11");
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Add(proxy);

        var ignored = await InvokeForwardedHeaders(options, untrusted, client);
        var accepted = await InvokeForwardedHeaders(options, proxy, client);

        Assert.Equal(untrusted, ignored);
        Assert.Equal(client, accepted);
    }

    [Fact]
    public void Reverse_proxy_requires_explicit_valid_proxy_addresses()
    {
        var validator = new ReverseProxyOptionsValidator();

        Assert.False(validator.Validate(null, new ReverseProxyOptions { Enabled = true }).Succeeded);
        Assert.False(validator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownProxies = ["not-an-ip"] }).Succeeded);
        Assert.True(validator.Validate(null, new ReverseProxyOptions { Enabled = true, KnownProxies = ["10.0.0.10"] }).Succeeded);
    }

    private static async Task<IPAddress?> InvokeForwardedHeaders(ForwardedHeadersOptions options, IPAddress sender, IPAddress client)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = sender;
        context.Request.Headers["X-Forwarded-For"] = client.ToString();
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);
        return context.Connection.RemoteIpAddress;
    }

    private static GymShopRequestLimiter CreateLimiter(int loginAccountLimit = 5, int paymentOrderLimit = 3)
    {
        var options = ValidOptions();
        options.LoginAccount.PermitLimit = loginAccountLimit;
        options.PaymentOrder.PermitLimit = paymentOrderLimit;
        return new GymShopRequestLimiter(Options.Create(options), TimeProvider.System);
    }

    private static GymShopRateLimitingOptions ValidOptions()
    {
        static RateLimitRule Rule(int permits = 10) => new() { PermitLimit = permits, WindowSeconds = 60 };
        return new GymShopRateLimitingOptions
        {
            Enabled = true,
            LoginIp = Rule(), LoginAccount = Rule(), RegistrationIp = Rule(), RegistrationGlobal = Rule(),
            PaymentUser = Rule(), PaymentOrder = Rule(), WebhookIp = Rule(), WebhookGlobal = Rule()
        };
    }
}
