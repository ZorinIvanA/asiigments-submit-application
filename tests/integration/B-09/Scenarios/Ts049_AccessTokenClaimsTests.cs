using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-049 «Access-токен: клеймы sub/role и TTL 15 минут» (happy_path, FR-008,
/// P0).
///
/// given: выполнен успешный вход учителя.
/// when:  декодирование payload access-токена без верификации подписи.
/// then:  sub=uuid пользователя; role='teacher'; exp−iat=900 (FR-008 AC
///        «Access-клеймы»).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth. Поведенческая часть кейса исполнима дословно и
/// исполнена в собственной зоне батча B-09 (прецедент c-1052); расхождение
/// размещения зафиксировано в scenario_change_requests.
/// </summary>
public sealed class Ts049_AccessTokenClaimsTests(B09TimedWebAppFactory factory) : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.49";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TeacherLogin_AccessTokenPayload_HasSubRoleAnd900SecondTtl()
    {
        // given: учитель существует (сид фикстуры — teacher/teacher123!) и
        // выполнил успешный вход.
        _ = _factory.Services;
        var teacher = _factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher");
        Assert.NotNull(teacher);

        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(login, HttpStatusCode.OK, "успешный вход учителя (TS-049)");

        // when: декодирование payload access-токена без верификации подписи.
        var access = B09AuthSupport.SingleCookie(login, "access_token", "Set-Cookie входа учителя (TS-049)");
        var payload = B09AuthSupport.DecodeJwtPayload(access.Value);

        // then: sub=uuid пользователя.
        Assert.Equal(JsonValueKind.String, payload.GetProperty("sub").ValueKind);
        Assert.Equal(teacher!.Id.ToString("D"), payload.GetProperty("sub").GetString());

        // then: role='teacher'.
        Assert.Equal("teacher", payload.GetProperty("role").GetString());

        // then: exp−iat=900 (Auth__AccessTtlMinutes=15 по умолчанию).
        var iat = payload.GetProperty("iat").GetInt64();
        var exp = payload.GetProperty("exp").GetInt64();
        Assert.Equal(900, exp - iat);
    }
}
