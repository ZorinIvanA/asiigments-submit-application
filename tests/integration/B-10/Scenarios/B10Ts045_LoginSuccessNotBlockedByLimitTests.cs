using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-045 (P0, happy_path; FR-007) «Вход: успех не блокируется исчерпанным лимитом
/// неудач» (актуальная нумерация кейсов батча B-10; родственный тест предыдущей
/// нумерации — Ts044_LoginSuccessNotBlockedByLimitTests).
/// given: 5 меток 'teacher|IP' (все — прошлые неудачи); счётчик обнулён.
/// when:  POST /auth/login {login:'teacher', password:'teacher123!'}.
/// then:  200 + cookie — верный пароль проходит несмотря на исчерпанный лимит
///        неудач ключа; меток по-прежнему 5; Δkdf=1 (AC FR-007 «Успех не
///        блокируется лимитером»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts045_LoginSuccessNotBlockedByLimitTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.45";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS045_Login_CorrectPasswordWithExhaustedFailureLimit_SucceedsWithoutNewMarks()
    {
        // given: 5 меток 'teacher|IP' — пять прошлых неудач; счётчик обнулён.
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));

        // when: вход с ВЕРНЫМ паролем при исчерпанном лимите неудач ключа.
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", B10AuthRequests.TeacherPassword);
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 200 + cookie — верный пароль проходит, успех лимитером не
        // опрашивается и не учитывается (FR-007).
        _ = await ApiAssert.ReadOkJsonAsync(response);
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            B10AuthRequests.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: меток по-прежнему 5 — успех метку не пишет; Δkdf=1.
        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
    }
}
