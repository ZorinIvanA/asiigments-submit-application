using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-046 (P0, negative; FR-007) «Вход: успешный вход не блокируется
/// исчерпанным лимитом неудач».
/// given: 5 меток 'teacher|IP' (все — прошлые неудачи: m1 = T0, m2..m5 у
///        границы окна при фиктивном времени); счётчик KDF обнулён.
/// when:  POST {login:'teacher', password:'teacher123!'}.
/// then:  200 + cookie (верный пароль проходит); меток по-прежнему 5; Δkdf=1
///        (FR-007 AC «Успех не блокируется лимитером»). «Меток по-прежнему 5»
///        фиксируется поведенчески: на T0+61 с (m1 вне окна, живы 4 метки
///        неудач) следующая неудача допускается — 401; оставь успех собственную
///        метку, попытка осталась бы заблокированной (429).
/// </summary>
public sealed class Ts046_LoginSuccessUnblockedByExhaustedLimitTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS046_Login_CorrectPasswordPassesDespiteExhaustedFailureLimit_WithoutNewMark()
    {
        // given: 5 меток 'teacher|IP' — все прошлые неудачи при фиктивном
        // времени; счётчик KDF обнулён снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        var firstMarkAt = await B11LoginMarks.FailOnceAsync(_factory.Time, client, "teacher", "nope123!");
        _factory.Time.Advance(TimeSpan.FromSeconds(59));
        _ = await B11LoginMarks.FailManyAsync(_factory.Time, client, "teacher", "nope123!", 4, TimeSpan.FromMilliseconds(100));

        // when: POST {login:'teacher', password:'teacher123!'} при исчерпанном
        // лимите неудач ключа.
        var before = kdf.Snapshot();
        using var success = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        var after = kdf.Snapshot();

        // then: 200 + cookie (верный пароль проходит); Δkdf=1.
        _ = await ApiAssert.ReadOkJsonAsync(success);
        Assert.True(
            HostClients.HasSetCookie(success, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            HostClients.HasSetCookie(success, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));

        // then: меток по-прежнему 5 — успех лимитера не касается и метку не
        // пишет: на T0+61 с живы ровно четыре метки неудач, следующая неудача
        // допускается (401); скрытая метка от успеха оставила бы окно
        // исчерпанным (429).
        _factory.Time.SetUtcNow(firstMarkAt + TimeSpan.FromSeconds(61));
        using var probe = await HostClients.LoginAsync(client, "teacher", "nope123!");
        await ApiAssert.AssertMessageAsync(
            probe,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
    }
}
