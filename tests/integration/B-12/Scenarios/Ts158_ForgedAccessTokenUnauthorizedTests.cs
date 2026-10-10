using System.Text;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-158 (P1, negative; FR-022, FR-008) «Подделанный access-токен — 401».
/// given: Валидный JWT чужой сессии; подпись изменена (изменён payload без
///        пересоздания подписи).
/// when:  GET /auth/me с cookie access_token=&lt;подделанный токен&gt;.
/// then:  401 «Не авторизован» (подпись HS256 проверяется; роль читается только
///        из проверенного клейма).
/// Подделка: claim sub payload меняется на другой uuid при НЕизменённой подписи —
/// валидация обязана отвергнуть токен целиком.
/// </summary>
public sealed class Ts158_ForgedAccessTokenUnauthorizedTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_WithTamperedJwtPayload_IsUnauthorized()
    {
        // given: валидный JWT валидной сессии (сид-преподаватель).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var validJwt = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher.Id, UserRoles.Teacher);

        // given: payload изменён без пересоздания подписи (sub → чужой uuid).
        var forgedJwt = TamperJwtClaim(validJwt, "sub", Guid.NewGuid().ToString());
        Assert.NotEqual(validJwt, forgedJwt);

        using var client = B12AuthSessions.CreateClient(_factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={forgedJwt}");

        // when: GET /auth/me с подделанным access-cookie.
        using var response = await client.GetAsync(B12AuthEndpoints.Me);

        // then: 401 «Не авторизован» — подпись проверяется, токен отвергнут целиком.
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }

    /// <summary>
    /// Пересобирает JWT с заменой строкового claim в payload БЕЗ пересоздания
    /// подписи (header и signature переиспользуются дословно).
    /// </summary>
    private static string TamperJwtClaim(string jwt, string claimName, string newValue)
    {
        var segments = jwt.Split('.');
        Assert.Equal(3, segments.Length);

        using var document = JsonDocument.Parse(WebEncoders.Base64UrlDecode(segments[1]));
        Assert.True(
            document.RootElement.TryGetProperty(claimName, out _),
            $"В payload отсутствует ожидаемый для подделки claim «{claimName}».");

        var properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name == claimName
                ? $"\"{claimName}\":\"{newValue}\""
                : $"{JsonEncodedText.Encode(property.Name).Value}:{property.Value.GetRawText()}")
            .ToList();
        var tamperedPayload = WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes($"{{{string.Join(",", properties)}}}"));
        return $"{segments[0]}.{tamperedPayload}.{segments[2]}";
    }
}
