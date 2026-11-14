using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки собственной схемы аутентификации (ADR-004, FR-011): cookie
/// access_token → principal с sub/role; отсутствие/невалидный токен и
/// непрозрачный reset-токен вместо access — запрос анонимен; Challenge —
/// детерминированный
/// 401 {"message":"Не авторизован"} с application/json.
/// </summary>
public sealed class CookieAuthenticationHandlerTests
{
    private const string JwtKey = "handler-test-jwt-signing-key-0123456789abcdef-0123456789abcdef";
    private static readonly Guid UserId = Guid.Parse("7f3d2c9a-1b4e-4f6a-9c8d-2e5f0a1b3c4d");

    [Fact]
    public async Task ValidAccessTokenCookie_Authenticates_WithSubAndRoleClaims()
    {
        var (context, tokenService) = CreateAuthenticatedContext();
        var token = tokenService.IssueAccessToken(UserId, "teacher");
        context.Request.Headers.Cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={token}";

        var (result, ticket) = await AuthenticateAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthCoreDefaults.AuthenticationScheme, ticket.AuthenticationScheme);
        var user = ticket.Principal;
        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal(UserId.ToString(), user.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("teacher", user.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task MissingCookie_NoResult()
    {
        var (context, _) = CreateAuthenticatedContext();

        var (result, _) = await AuthenticateAsync(context);

        Assert.True(result.None);
    }

    // ------------------------------------------------------------------
    // Канал subject (аменда CR-001/ADR-044, IF-016): claim «login» в principal.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidAccessTokenCookie_WithLogin_PropagatesLoginClaimToPrincipal()
    {
        var (context, tokenService) = CreateAuthenticatedContext();
        var token = tokenService.IssueAccessToken(UserId, "student", "student01");
        context.Request.Headers.Cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={token}";

        var (result, ticket) = await AuthenticateAsync(context);

        // Непустой Login прокидывается в principal ровно одним claim «login».
        Assert.True(result.Succeeded);
        var loginClaim = Assert.Single(ticket.Principal.Claims
            .Where(claim => claim.Type == AuthCoreDefaults.LoginClaimType)
            .ToList());
        Assert.Equal("student01", loginClaim.Value);
        Assert.Equal("student01", ticket.Principal.FindFirstValue(AuthCoreDefaults.LoginClaimType));
    }

    [Fact]
    public async Task ValidAccessTokenCookie_WithoutLogin_PrincipalHasNoLoginClaim()
    {
        // Толерантность IF-003: токен харнеса без claim «login» аутентифицируется,
        // principal без subject — поведение идентично состоянию до аменды.
        var (context, tokenService) = CreateAuthenticatedContext();
        var token = tokenService.IssueAccessToken(UserId, "student");
        context.Request.Headers.Cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={token}";

        var (result, ticket) = await AuthenticateAsync(context);

        Assert.True(result.Succeeded);
        Assert.True(ticket.Principal.Identity?.IsAuthenticated);
        Assert.Null(ticket.Principal.FindFirstValue(AuthCoreDefaults.LoginClaimType));
    }

    [Fact]
    public async Task InvalidAccessToken_NoResult()
    {
        var (context, _) = CreateAuthenticatedContext();
        context.Request.Headers.Cookie = $"{AuthCoreDefaults.AccessTokenCookieName}=garbage-token";

        var (result, _) = await AuthenticateAsync(context);

        Assert.True(result.None);
    }

    [Fact]
    public async Task ResetTokenCookie_NoResult_OpaqueTokenIsNotAccessJwt()
    {
        var (context, tokenService) = CreateAuthenticatedContext();
        var reset = tokenService.CreatePasswordResetToken(UserId);
        context.Request.Headers.Cookie = $"{AuthCoreDefaults.AccessTokenCookieName}={reset.Value}";

        var (result, _) = await AuthenticateAsync(context);

        // Непрозрачный reset-токен (ASM-002) не разбирается как access-JWT —
        // запрос анонимен.
        Assert.True(result.None);
    }

    [Fact]
    public async Task Challenge_WritesDeterministicUnauthorizedEnvelope()
    {
        var (context, _) = CreateAuthenticatedContext();
        context.Response.Body = new MemoryStream();

        var handler = await CreateHandlerAsync(context);
        await handler.ChallengeAsync(new AuthenticationProperties());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType?.Split(';')[0]);

        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd());
        var message = document.RootElement.GetProperty("message").GetString();
        Assert.Equal(AuthCoreDefaults.UnauthorizedMessage, message);
    }

    private static (DefaultHttpContext Context, ITokenService Tokens) CreateAuthenticatedContext()
    {
        var context = new DefaultHttpContext { RequestServices = BuildServices() };
        return (context, context.RequestServices.GetRequiredService<ITokenService>());
    }

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddAuthCore()
            .Configure<AuthOptions>(options => options.JwtKey = JwtKey)
            .BuildServiceProvider();

    private static async Task<CookieAuthenticationHandler> CreateHandlerAsync(DefaultHttpContext context)
    {
        var handler = new CookieAuthenticationHandler(
            new StubSchemeOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(AuthCoreDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
            context);
        return handler;
    }

    private static async Task<(AuthenticateResult Result, AuthenticationTicket Ticket)> AuthenticateAsync(
        DefaultHttpContext context)
    {
        var handler = await CreateHandlerAsync(context);
        var result = await handler.AuthenticateAsync();
        return (result, result.Ticket!);
    }

    private sealed class StubSchemeOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
