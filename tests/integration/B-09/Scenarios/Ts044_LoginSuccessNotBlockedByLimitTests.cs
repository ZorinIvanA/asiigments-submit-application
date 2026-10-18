using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-044 (P0, happy_path; FR-007) «Вход: успех не блокируется исчерпанным
/// лимитом неудач».
/// given: 5 меток 'teacher|IP' (все — прошлые неудачи); счётчик KDF сброшен.
/// when:  POST {login:'teacher', password:'teacher123!'}.
/// then:  200 + cookie (верный пароль проходит несмотря на исчерпанный лимит
///        ключа); меток по-прежнему 5; Δkdf=1. FR-007 AC «Успех не блокируется
///        лимитером».
/// </summary>
public sealed class Ts044_LoginSuccessNotBlockedByLimitTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.44";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS044_Login_CorrectPasswordWithExhaustedFailureLimit_SucceedsAndWritesNoMark()
    {
        // given: 5 меток 'teacher|IP' — пять прошлых неудач; счётчик KDF сброшен.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));

        // when: вход с ВЕРНЫМ паролем при исчерпанном лимите ключа.
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var response = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 200 MeDto + обе cookie — верный пароль проходит, успех лимитером
        // не опрашивается (FR-007).
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.OK, "TS-044: вход teacher");
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.True(
            B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: меток по-прежнему 5 — успех метку не пишет; Δkdf=1.
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));
    }
}
