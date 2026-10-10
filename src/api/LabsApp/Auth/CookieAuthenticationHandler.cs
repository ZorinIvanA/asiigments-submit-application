using System.Security.Claims;
using System.Text.Encodings.Web;
using LabsApp.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LabsApp.Auth;

/// <summary>
/// Собственная схема аутентификации по cookie access_token (ADR-004, FR-011):
/// отсутствие cookie или невалидный токен (в т.ч. роль вне {student, teacher},
/// reset-токен вместо access) — запрос анонимен, обращение к защищённому
/// эндпойнту получает Challenge с ДЕТЕРМИНИРОВАННЫМ телом 401
/// {"message":"Не авторизован"} (неявные тела JwtBearer недопустимы, IF-001).
/// Успешная аутентификация строит principal с claims имени (sub), роли и — при
/// непустом Login токена — claim «login» (канал subject, аменда CR-001/ADR-044).
/// </summary>
public sealed class CookieAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Context.Request.Cookies[AuthCoreDefaults.AccessTokenCookieName];
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tokens = Context.RequestServices.GetRequiredService<ITokenService>();
        var payload = tokens.ValidateAccessToken(token);
        if (payload is null)
        {
            // Невалидный access — запрос анонимен; тело 401 даст Challenge (FR-011).
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.UserId.ToString()),
            new(ClaimTypes.Role, payload.Role),
        };
        if (!string.IsNullOrEmpty(payload.Login))
        {
            // Канал subject (аменда CR-001/ADR-044, IF-016): непустой Login
            // прокидывается в principal claim'ом «login» — потребители
            // наблюдаемости (ObservabilityMiddleware/SecurityEventLogger) читают
            // его как subject записей «Api.Security». Токен без claim — principal
            // без subject (толерантность IF-003).
            claims.Add(new Claim(AuthCoreDefaults.LoginClaimType, payload.Login));
        }

        var identity = new ClaimsIdentity(claims, AuthCoreDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <inheritdoc/>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ErrorEnvelope { Message = AuthCoreDefaults.UnauthorizedMessage });
    }
}
