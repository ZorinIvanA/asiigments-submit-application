using LabsApp.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки ICookieService (IF-004, FR-008/NFR-007/ADR-009): обе cookie —
/// HttpOnly, SameSite=Strict, Path=/; Max-Age access_token=900,
/// refresh_token=604800; Secure — если и только если окружение ≠ Development
/// (Staging/Production — Secure, Development — нет); ClearAuth — Max-Age=0
/// с теми же атрибутами.
/// </summary>
public sealed class CookieServiceTests
{
    [Fact]
    public void SetAccess_Development_PathRoot_MaxAge900_NoSecure()
    {
        var context = new DefaultHttpContext();
        var service = new CookieService(Environment("Development"));

        service.SetAccess(context, "access-jwt-value");

        var access = SingleCookie(context, "access_token=");
        Assert.Contains("path=/", access, StringComparison.Ordinal);
        Assert.Contains("max-age=900", access, StringComparison.Ordinal);
        Assert.Contains("httponly", access, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", access, StringComparison.Ordinal);
        Assert.DoesNotContain("secure", access, StringComparison.Ordinal);
    }

    [Fact]
    public void SetRefresh_Development_PathRoot_MaxAge604800_NoSecure()
    {
        var context = new DefaultHttpContext();
        var service = new CookieService(Environment("Development"));

        service.SetRefresh(context, "refresh-value");

        var refresh = SingleCookie(context, "refresh_token=");
        Assert.Contains("path=/", refresh, StringComparison.Ordinal);
        Assert.Contains("max-age=604800", refresh, StringComparison.Ordinal);
        Assert.Contains("httponly", refresh, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("secure", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void SetCookies_OutsideDevelopment_Secure()
    {
        // Правило v2.2 — «Secure iff окружение ≠ Development»: и Production, и
        // промежуточное Staging получают Secure.
        foreach (var environmentName in new[] { "Production", "Staging" })
        {
            var context = new DefaultHttpContext();
            var service = new CookieService(Environment(environmentName));

            service.SetAccess(context, "a");
            service.SetRefresh(context, "r");

            var cookies = context.Response.Headers.SetCookie.ToArray();
            Assert.Equal(2, cookies.Length);
            Assert.All(cookies, cookie =>
            {
                Assert.Contains("secure", cookie, StringComparison.Ordinal);
                Assert.Contains("httponly", cookie, StringComparison.Ordinal);
                Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
                Assert.Contains("path=/", cookie, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void ClearAuth_BothCookies_MaxAgeZero_WithSameAttributes()
    {
        var context = new DefaultHttpContext();
        var service = new CookieService(Environment("Production"));

        service.ClearAuth(context);

        var cookies = context.Response.Headers.SetCookie.ToArray();
        Assert.Equal(2, cookies.Length);
        foreach (var cookie in cookies)
        {
            Assert.Contains("max-age=0", cookie, StringComparison.Ordinal);
            Assert.Contains("path=/", cookie, StringComparison.Ordinal);
            Assert.Contains("httponly", cookie, StringComparison.Ordinal);
            Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
            Assert.Contains("secure", cookie, StringComparison.Ordinal);
        }

        _ = SingleCookie(context, "access_token=");
        _ = SingleCookie(context, "refresh_token=");
    }

    [Fact]
    public void SetArguments_Throw()
    {
        var service = new CookieService(Environment("Development"));

        Assert.Throws<ArgumentNullException>(() => service.SetAccess(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => service.SetRefresh(null!, "r"));
        Assert.Throws<ArgumentException>(() => service.SetAccess(new DefaultHttpContext(), string.Empty));
        Assert.Throws<ArgumentException>(() => service.SetRefresh(new DefaultHttpContext(), string.Empty));
        Assert.Throws<ArgumentNullException>(() => service.ClearAuth(null!));
    }

    private static string SingleCookie(DefaultHttpContext context, string prefix) =>
        context.Response.Headers.SetCookie
            .SingleOrDefault(cookie => cookie!.StartsWith(prefix, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Cookie с префиксом '{prefix}' не найдена.");

    private static IHostEnvironment Environment(string environmentName) =>
        new TestHostEnvironment(environmentName);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "LabsApp.Tests";

        public string EnvironmentName { get; set; } = environmentName;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>
/// Юнит-проверки IClientIpResolver (IF-006, ADR-006): ТОЛЬКО
/// Connection.RemoteIpAddress (X-Forwarded-For игнорируется — резолвер его
/// вообще не читает), null → ''.
/// </summary>
public sealed class ClientIpResolverTests
{
    [Fact]
    public void GetClientIp_ReturnsRemoteIpAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", new ClientIpResolver().GetClientIp(context));
    }

    [Fact]
    public void GetClientIp_ForwardedHeaderIsIgnored_ConnectionAddressWins()
    {
        // ADR-006: X-Forwarded-For не разбирается — ключ лимитера строится
        // только по адресу соединения.
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.9");
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4";

        Assert.Equal("198.51.100.9", new ClientIpResolver().GetClientIp(context));
    }

    [Fact]
    public void GetClientIp_MissingAddress_ReturnsEmptyString()
    {
        var context = new DefaultHttpContext();

        Assert.Equal(string.Empty, new ClientIpResolver().GetClientIp(context));
    }

    [Fact]
    public void GetClientIp_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientIpResolver().GetClientIp(null!));
    }
}
