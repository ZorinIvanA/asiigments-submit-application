using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-040 (P0, happy_path; FR-007, FR-008) «Вход: успех — 200 MeDto, cookie,
/// Δkdf=1, меток нет» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts039_LoginSuccessTests).
/// given: Пользователь teacher/teacher123! существует (сид); меток лимитера нет;
///        счётчик KDF обнулён (снимком до запроса — стартовые деривации в Δ не
///        входят).
/// when:  POST /api/v1/auth/login {login:'teacher', password:'teacher123!'}.
/// then:  200 MeDto {login:'teacher', role:'teacher', groupName:null}; оба cookie
///        установлены; Δkdf(login)=1; меток неудач по ключу 'teacher|IP' не
///        добавлено — успешный вход лимитером не учитывается (AC FR-007
///        «Успешный вход»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts040_LoginSuccessTests(B10TimedWebAppFactory factory) : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.40";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS040_Login_WithSeededTeacher_Returns200MeDtoBothCookiesDeltaKdf1AndNoMarks()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий
        // хост); счётчик KDF обнулён снимком до запроса.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /api/v1/auth/login {login:'teacher', password:'teacher123!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);

        // then: 200 MeDto {login:'teacher', role:'teacher', groupName:null}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("groupName").ValueKind);

        // then: оба cookie установлены.
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: Δkdf(login)=1 — ровно одна деривация с меткой вызывателя 'login'.
        var after = B10AuthGates.KdfSnapshot(_factory);
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(1L, B10AuthGates.Delta(before, after, KdfCallers.Login));

        // then: меток неудач по ключу 'teacher|IP' не добавлено — успешный вход
        // лимитером не учитывается (AC FR-007 «Успешный вход»).
        Assert.Equal(0, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
    }
}
