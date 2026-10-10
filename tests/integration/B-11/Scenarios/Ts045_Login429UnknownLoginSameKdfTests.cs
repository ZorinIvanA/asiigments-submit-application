using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-045 (P0, negative; FR-007, FR-005) «Вход: 429 на неизвестном логине —
/// равная стоимость веток».
/// given: 5 меток ключа 'ghost|IP' в текущем окне (сеются эталонными
///        деривациями VerifyReference — на сеялку Δkdf не проверяется);
///        счётчик KDF обнулён.
/// when:  POST {login:'ghost', password:'whatever1!'}.
/// then:  429 'Слишком много попыток. Повторите позже'; Δkdf=1 — столько же
///        дериваций, сколько в сценарии 429 на известном логине (TS-044):
///        равная стоимость веток (FR-007 AC «429 на неизвестном логине»).
/// </summary>
public sealed class Ts045_Login429UnknownLoginSameKdfTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS045_Login_429OnUnknownLogin_PerformsSameSingleKdfAsKnownLoginBranch()
    {
        // given: 5 меток 'ghost|IP' в текущем окне при фиктивном времени
        // (m1 = T0, затем 59 с простоя и четыре метки по 100 мс); счётчик KDF
        // обнулён снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        _ = await B11LoginMarks.FailOnceAsync(_factory.Time, client, "ghost", "whatever1!");
        _factory.Time.Advance(TimeSpan.FromSeconds(59));
        _ = await B11LoginMarks.FailManyAsync(_factory.Time, client, "ghost", "whatever1!", 4, TimeSpan.FromMilliseconds(100));

        // when: 6-я неудача ghost (ShouldBlock=true) при обнулённом снимком
        // счётчике.
        var before = kdf.Snapshot();
        using var blocked = await HostClients.LoginAsync(client, "ghost", "whatever1!");
        var after = kdf.Snapshot();

        // then: 429 'Слишком много попыток. Повторите позже'; Δkdf=1 — столько
        // же дериваций, сколько в 429-ветке известного логина (TS-044): равная
        // стоимость веток.
        await ApiAssert.AssertMessageAsync(
            blocked,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(before, after));
    }
}
