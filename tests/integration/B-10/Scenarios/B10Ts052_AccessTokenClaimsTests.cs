using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-052 «Access-клеймы: sub, role, exp−iat=900» (happy_path, FR-008, P0).
///
/// given: выпущенный access-токен пользователя teacher (значение cookie
///        access_token успешного входа сеяного учителя).
/// when:  декодирование payload JWT без верификации подписи.
/// then:  sub = uuid пользователя; role='teacher'; exp−iat = 900 (FR-008 AC
///        «Access-клеймы»).
/// </summary>
public sealed class B10Ts052_AccessTokenClaimsTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory;

    public B10Ts052_AccessTokenClaimsTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task IssuedAccessTokenCarriesTeacherSub_RoleAndFifteenMinuteLifetime()
    {
        // given: выпущенный при входе access-токен пользователя teacher (значение
        // cookie access_token успешного входа).
        using var client = HostClients.Create(_factory);
        using var response = await client.PostAsJsonAsync(
            B10CookieFlow.LoginPath,
            new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = SeedOptions.DefaultTeacherPassword,
            });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: успешный вход сеяным учителем — ожидался 200, фактически " +
            $"{(int)response.StatusCode} {response.StatusCode}.");
        var accessToken = B10CookieFlow
            .RequireCookie(B10SetCookieReader.Read(response), AuthCoreDefaults.AccessTokenCookieName, "login")
            .Value;

        // when: декодирование payload JWT без верификации подписи.
        var claims = DecodePayload(accessToken);

        // then: sub = uuid пользователя (сеяный учитель).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сеяный учитель не найден в IUserRepository — шаг then «sub = uuid " +
                "пользователя» неисполним.");
        var sub = claims.GetProperty("sub").GetString();
        Assert.True(
            sub is not null && Guid.TryParse(sub, out var subId) && subId == teacher.Id,
            $"Claim sub должен быть uuid сеяного учителя ({teacher.Id}), фактически «{sub}».");

        // then: role='teacher'.
        var role = claims.GetProperty("role").GetString();
        Assert.True(
            string.Equals(role, UserRoles.Teacher, StringComparison.Ordinal),
            $"Claim role сеяного учителя должен быть «{UserRoles.Teacher}», фактически «{role}».");

        // then: exp−iat = 900 (Auth__AccessTtlMinutes умолчание 15 минут).
        var iat = claims.GetProperty("iat").GetInt64();
        var exp = claims.GetProperty("exp").GetInt64();
        Assert.True(
            exp - iat == 900,
            $"Разница exp−iat должна быть 900 секунд, фактически {exp - iat} (iat={iat}, exp={exp}).");
    }

    /// <summary>
    /// Payload JWT (второй сегмент compact-сериализации) без проверки подписи:
    /// base64url-декодирование и разбор JSON-объекта клеймов.
    /// </summary>
    private static JsonElement DecodePayload(string accessToken)
    {
        var segments = accessToken.Split('.');
        Assert.True(
            segments.Length == 3,
            $"Значение access-токена должно быть JWT из 3 сегментов, фактически {segments.Length}.");
        var payload = Encoding.UTF8.GetString(DecodeBase64Url(segments[1]));
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>Декодирование base64url-сегмента JWT (без символов дополнения).</summary>
    private static byte[] DecodeBase64Url(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String((base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        });
    }
}
