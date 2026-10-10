using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-041 (P0, happy_path; FR-007, FR-008) «Вход: успех 200 MeDto + cookie +
/// 1 KDF, лимитер не учитывает».
/// given: Development, демо-сид: teacher/teacher123! существует; меток лимитера
///        нет; счётчик KDF обнулён (снимком до запроса — стартовые деривации
///        сида в Δ не входят).
/// when:  POST /api/v1/auth/login {login:'teacher', password:'teacher123!'}.
/// then:  200 MeDto {login:'teacher', role:'teacher', groupName:null}; установлены
///        оба cookie; Δkdf по метке 'login' = 1; меток неудач по ключу
///        'teacher|IP' не добавлено (FR-007 AC «Успешный вход»). Метки
///        фиксируются поведенчески: пять последующих неудач все допускаются
///        (401) — оставь успех собственную метку, пятая неудача упёрлась бы
///        в лимит 5 и ответила 429.
/// </summary>
public sealed class Ts041_LoginSuccess200CookiesOneKdfTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS041_Login_WithSeededTeacher_Returns200MeDtoBothCookiesAndExactlyOneLoginKdf()
    {
        // given: teacher/teacher123! существует (демо-сид); меток лимитера нет
        // (свежий хост); счётчик KDF обнулён снимком до запроса.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        var before = kdf.Snapshot();
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/auth/login {login:'teacher', password:'teacher123!'}.
        using var response = await HostClients.LoginAsync(client, "teacher", "teacher123!");

        // then: 200 MeDto {login:'teacher', role:'teacher', groupName:null}.
        var body = await ApiAssert.ReadOkJsonAsync(response);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("groupName").ValueKind);

        // then: оба cookie установлены.
        Assert.True(
            HostClients.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
            "Успешный вход не установил cookie access_token.");
        Assert.True(
            HostClients.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
            "Успешный вход не установил cookie refresh_token.");

        // then: Δkdf по метке 'login' = 1 (ровно одна деривация).
        var after = kdf.Snapshot();
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));
        Assert.Equal(1L, B11Kdf.CallerDelta(before, after, "login"));

        // then: меток неудач по ключу 'teacher|IP' не добавлено — успешный вход
        // лимитером не опрашивается и не учитывается (FR-007): пять последующих
        // неудач все 401 (лимит 5 не исчерпан скрытой меткой от успеха).
        for (var i = 0; i < 5; i++)
        {
            using var failure = await HostClients.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
    }
}
