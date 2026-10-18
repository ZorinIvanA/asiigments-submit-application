using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-039 (P0, happy_path; FR-007, FR-027) «Вход: успех — MeDto, cookie, Δkdf=1,
/// лимитер не опрашивается».
/// given: Пользователь teacher/teacher123! существует (сид); меток лимитера нет;
///        счётчик KDF сброшен (снимком до запроса — стартовые деривации в Δ не
///        входят).
/// when:  POST /auth/login {login:'teacher', password:'teacher123!'}.
/// then:  200 MeDto {login:'teacher', role:'teacher', groupName:null}; установлены
///        оба cookie; Δkdf=1; меток неудач не добавлено (FR-007 AC «Успешный вход»).
/// </summary>
public sealed class Ts039_LoginSuccessTests(B09TimedWebAppFactory factory) : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.39";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS039_Login_WithSeededTeacher_ReturnsMeDtoBothCookiesDeltaKdf1AndNoFailureMarks()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий
        // хост); счётчик KDF «сброшен» снимком до запроса.
        _ = _factory.Services;
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: POST /auth/login {login:'teacher', password:'teacher123!'}.
        using var response = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);

        // then: 200 MeDto {login:'teacher', role:'teacher', groupName:null}.
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.OK, "TS-039: вход teacher");
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("groupName").ValueKind);

        // then: установлены оба cookie.
        Assert.True(
            B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: Δkdf=1 — ровно одна деривация, с меткой вызывателя 'login' (FR-027).
        var after = B09KdfSeams.KdfSnapshot(_factory);
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));
        Assert.Equal(1L, B09KdfSeams.Delta(before, after, "login"));

        // then: меток неудач не добавлено — успешный вход лимитером не опрашивается
        // и не учитывается (FR-007).
        Assert.Equal(
            0, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));
    }
}
