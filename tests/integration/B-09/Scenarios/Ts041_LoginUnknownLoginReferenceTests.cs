using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

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
public sealed class Ts041_LoginUnknownLoginReferenceTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.41";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS041_Login_UnknownLogin_ReturnsUnified401DeltaKdf1AndWritesGhostMark()
    {
        // given: 'ghost' не существует; счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: POST {login:'ghost', password:'whatever1!'}.
        using var response = await B09AuthHttp.LoginAsync(client, "ghost", "whatever1!");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 401 с тем же текстом, что и при неверном пароле (единый 401).
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.Unauthorized, "TS-041: вход ghost");
        B09Assertions.MessageIs(body, "Неверный логин или пароль");

        // then: Δkdf=1 — эталонная деривация VerifyReference (метка reference),
        // ровно одна, как и в ветке Verify при найденном пользователе.
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));
        Assert.Equal(1L, B09KdfSeams.Delta(before, after, "reference"));

        // then: метка по ключу 'ghost|IP' записана (наблюдение кейса TS-040):
        // ещё 4 неудачи ghost допускаются (метки 2..5), 6-я попытка даёт 429.
        Assert.Equal(1, B09LoginMarkStore.MarksCount(_factory, "ghost", TestIp));
        for (var i = 0; i < 4; i++)
        {
            using var next = await B09AuthHttp.LoginAsync(client, "ghost", "whatever1!");
            Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        }

        using var sixth = await B09AuthHttp.LoginAsync(client, "ghost", "whatever1!");
        var sixthBody = await B09Assertions.ParseObjectAsync(
            sixth, HttpStatusCode.TooManyRequests, "TS-041: 6-я неудачная попытка ghost");
        B09Assertions.MessageIs(sixthBody, "Слишком много попыток. Повторите позже");
    }
}
