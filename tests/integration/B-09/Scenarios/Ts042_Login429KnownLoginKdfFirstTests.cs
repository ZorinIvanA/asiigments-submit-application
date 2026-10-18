using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-042 (P0, negative; FR-007, FR-004, FR-027) «Вход: 429 на известном логине —
/// KDF выполнен, метка не дописана».
/// given: 5 меток 'teacher|IP' в текущем окне (сеются пятью неудачами при
///        фиктивном времени — окно не продвигается, все метки в одном окне);
///        счётчик KDF сброшен.
/// when:  POST {login:'teacher', password:'nope123!'}.
/// then:  429 'Слишком много попыток. Повторите позже'; Δkdf=1 (KDF выполнен ДО
///        ShouldBlock); число меток осталось 5. FR-007 AC «429 на известном
///        логине — KDF выполнен».
/// </summary>
public sealed class Ts042_Login429KnownLoginKdfFirstTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.42";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS042_Login_429OnKnownLogin_PerformsKdfFirstAndWritesNoMark()
    {
        // given: 5 меток 'teacher|IP' в текущем окне.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));

        // when: 6-я неудача при обнулённом снимком счётчике KDF.
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var blocked = await B09AuthHttp.LoginAsync(client, "teacher", "nope123!");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 429 'Слишком много попыток. Повторите позже'; Δkdf=1 — KDF
        // выполняется ДО ShouldBlock (SEC-001).
        var body = await B09Assertions.ParseObjectAsync(blocked, HttpStatusCode.TooManyRequests, "TS-042: вход");
        B09Assertions.MessageIs(body, "Слишком много попыток. Повторите позже");
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));

        // then: число меток осталось 5 — отказ метку НЕ дописывает (окно не продлевается).
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "teacher", TestIp));
    }
}
