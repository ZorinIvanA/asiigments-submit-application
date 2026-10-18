using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

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
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts042_Login429KnownLoginKdfFirstTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.42";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS042_Login_429OnKnownLogin_PerformsKdfFirstAndWritesNoMark()
    {
        // given: 5 меток 'teacher|IP' в текущем окне.
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));

        // when: 6-я неудача при обнулённом снимком счётчике KDF.
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var blocked = await B10AuthRequests.LoginAsync(client, "teacher", "nope123!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 429 'Слишком много попыток. Повторите позже'; Δkdf=1 — KDF
        // выполняется ДО ShouldBlock (SEC-001).
        await ApiAssert.AssertMessageAsync(
            blocked, HttpStatusCode.TooManyRequests, "Слишком много попыток. Повторите позже");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");

        // then: число меток осталось 5 — отказ метку НЕ дописывает (окно не продлевается).
        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "teacher", TestIp));
    }
}
