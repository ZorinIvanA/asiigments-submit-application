using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-040 (P0, negative; FR-007, FR-027) «Вход: неверный пароль при известном
/// логине — 401, Δkdf=1, метка записана».
/// given: teacher существует; меток нет; счётчик KDF сброшен.
/// when:  POST {login:'teacher', password:'nope123!'}.
/// then:  401 'Неверный логин или пароль'; Δkdf=1; метка по ключу 'teacher|IP'
///        записана (наблюдение: после ещё 4 неудач 6-я попытка даёт 429).
///        FR-007 AC «Неверный пароль, известный логин».
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts040_LoginWrongPasswordMarkWrittenTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.40";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS040_Login_WrongPasswordKnownLogin_Returns401DeltaKdf1AndWritesMark()
    {
        // given: teacher существует; меток нет (свежий хост); счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST {login:'teacher', password:'nope123!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 401 'Неверный логин или пароль'; Δkdf=1.
        _ = await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");

        // then: метка по ключу 'teacher|IP' записана.
        Assert.Equal(1, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));

        // then: наблюдение кейса — после ещё 4 неудач (метки 2..5, все 401) 6-я
        // попытка даёт 429: лимит 5 за окно 60 с исчерпан.
        for (var i = 0; i < 4; i++)
        {
            using var next = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        }

        using var sixth = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
        await ApiAssert.AssertMessageAsync(
            sixth, HttpStatusCode.TooManyRequests, "Слишком много попыток. Повторите позже");
    }
}
