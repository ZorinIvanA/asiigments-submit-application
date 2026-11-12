using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-040 (P0, negative; FR-007, FR-027) «Вход: неверный пароль при известном
/// логине — 401, Δkdf=1, метка записана».
/// given: teacher существует; меток нет; счётчик KDF сброшен.
/// when:  POST {login:'teacher', password:'nope123!'}.
/// then:  401 'Неверный логин или пароль'; Δkdf=1; метка по ключу 'teacher|IP'
///        записана (наблюдение: после ещё 4 неудач 6-я попытка даёт 429).
///        FR-007 AC «Неверный пароль, известный логин».
/// </summary>
public sealed class Ts040_LoginWrongPasswordMarkWrittenTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.40";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS040_Login_WrongPasswordKnownLogin_Returns401DeltaKdf1AndWritesMark()
    {
        // given: teacher существует; меток нет (свежий хост); счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: POST {login:'teacher', password:'nope123!'}.
        using var response = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 401 'Неверный логин или пароль'; Δkdf=1.
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.Unauthorized, "TS-040: вход");
        B09Assertions.MessageIs(body, "Неверный логин или пароль");
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));

        // then: метка по ключу 'teacher|IP' записана.
        Assert.Equal(1, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));

        // then: наблюдение кейса — после ещё 4 неудач (метки 2..5, все 401) 6-я
        // попытка даёт 429: лимит 5 за окно 60 с исчерпан.
        for (var i = 0; i < 4; i++)
        {
            using var next = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        }

        using var sixth = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
        var sixthBody = await B09Assertions.ParseObjectAsync(
            sixth, HttpStatusCode.TooManyRequests, "TS-040: 6-я неудачная попытка");
        B09Assertions.MessageIs(sixthBody, "Слишком много попыток. Повторите позже");
    }
}
