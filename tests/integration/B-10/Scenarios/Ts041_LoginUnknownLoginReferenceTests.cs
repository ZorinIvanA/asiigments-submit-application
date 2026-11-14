using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-041 (P0, negative; FR-007, FR-027) «Вход: неизвестный логин — единый 401,
/// эталонная деривация».
/// given: Пользователя с lower(login)='ghost' не существует (демо-набор выключен,
///        сид — только teacher); счётчик KDF сброшен.
/// when:  POST {login:'ghost', password:'whatever1!'}.
/// then:  401 'Неверный логин или пароль' (тот же текст, что при неверном пароле);
///        Δkdf=1 (VerifyReference); метка по ключу 'ghost|IP' записана.
///        FR-007 AC «Неизвестный логин».
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts041_LoginUnknownLoginReferenceTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.41";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS041_Login_UnknownLogin_ReturnsUnified401DeltaKdf1AndWritesGhostMark()
    {
        // given: 'ghost' не существует; счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST {login:'ghost', password:'whatever1!'}.
        using var response = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 401 с тем же текстом, что и при неверном пароле (единый 401).
        _ = await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.Unauthorized, "Неверный логин или пароль");

        // then: Δkdf=1 — эталонная деривация VerifyReference (метка reference),
        // ровно одна, как и в ветке Verify при найденном пользователе.
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(1L, B10AuthGates.Delta(before, after, KdfCallers.Reference));

        // then: метка по ключу 'ghost|IP' записана (наблюдение кейса TS-040):
        // ещё 4 неудачи ghost допускаются (метки 2..5), 6-я попытка даёт 429.
        Assert.Equal(1, B10AuthGates.LoginMarksCount(_factory, "ghost", TestIp));
        for (var i = 0; i < 4; i++)
        {
            using var next = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
            Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        }

        using var sixth = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
        await ApiAssert.AssertMessageAsync(
            sixth, HttpStatusCode.TooManyRequests, "Слишком много попыток. Повторите позже");
    }
}
