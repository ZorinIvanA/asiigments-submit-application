using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

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
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts039_LoginSuccessTests(B10TimedWebAppFactory factory) : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.39";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS039_Login_WithSeededTeacher_ReturnsMeDtoBothCookiesDeltaKdf1AndNoFailureMarks()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий
        // хост); счётчик KDF «сброшен» снимком до запроса.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /auth/login {login:'teacher', password:'teacher123!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);

        // then: 200 MeDto {login:'teacher', role:'teacher', groupName:null}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("groupName").ValueKind);

        // then: установлены оба cookie.
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: Δkdf=1 — ровно одна деривация, с меткой вызывателя 'login' (FR-027).
        var after = B10AuthGates.KdfSnapshot(_factory);
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(1L, B10AuthGates.Delta(before, after, KdfCallers.Login));

        // then: меток неудач не добавлено — успешный вход лимитером не опрашивается
        // и не учитывается (FR-007).
        Assert.Equal(0, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
    }
}
