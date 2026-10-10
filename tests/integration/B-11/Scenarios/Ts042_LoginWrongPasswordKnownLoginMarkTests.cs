using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-042 (P0, negative; FR-007) «Вход: неверный пароль известного логина —
/// 401, метка записана».
/// given: teacher существует; меток нет; счётчик KDF обнулён.
/// when:  POST {login:'teacher', password:'nope123!'}.
/// then:  401, message 'Неверный логин или пароль'; Δkdf=1; метка по ключу
///        'teacher|IP' записана (FR-007 AC). Метка фиксируется поведенчески
///        (наблюдение из кейса): после ещё четырёх неудач 6-я попытка даёт 429.
/// </summary>
public sealed class Ts042_LoginWrongPasswordKnownLoginMarkTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS042_Login_WrongPasswordOnKnownLogin_ReturnsUnified401AndRecordsMark()
    {
        // given: teacher существует (демо-сид); меток нет (свежий хост); счётчик
        // KDF обнулён снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        // when: POST {login:'teacher', password:'nope123!'}.
        var before = kdf.Snapshot();
        using var response = await HostClients.LoginAsync(client, "teacher", "nope123!");
        var after = kdf.Snapshot();

        // then: 401, message 'Неверный логин или пароль'; Δkdf=1.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));

        // then: метка по ключу 'teacher|IP' записана: ещё четыре неудачи
        // допускаются (всего в окне 5 меток), 6-я попытка даёт 429.
        for (var i = 0; i < 4; i++)
        {
            using var failure = await HostClients.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var sixth = await HostClients.LoginAsync(client, "teacher", "nope123!");
        await ApiAssert.AssertMessageAsync(
            sixth,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже",
            exactSingleMessageProperty: true);
    }
}
