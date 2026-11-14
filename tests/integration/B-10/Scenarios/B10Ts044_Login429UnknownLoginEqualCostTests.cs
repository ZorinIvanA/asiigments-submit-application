using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-044 (P0, negative; FR-007, FR-004) «Вход: 429 на неизвестном логине — та же
/// стоимость KDF» (актуальная нумерация кейсов батча B-10; родственный тест
/// предыдущей нумерации — Ts043_Login429UnknownLoginEqualCostTests).
/// given: 5 меток 'ghost|IP' в текущем окне; счётчик обнулён.
/// when:  POST /auth/login {login:'ghost', password:'whatever1!'}.
/// then:  429; Δkdf=1 — столько же, сколько в ветке 429 на известном логине
///        (TS-043): равная стоимость веток (AC FR-007 «429 на неизвестном
///        логине — KDF выполнен»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts044_Login429UnknownLoginEqualCostTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.44";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS044_Login_429OnUnknownLogin_CostsSameSingleKdfAsKnownLoginBranch()
    {
        // given: 5 меток 'ghost|IP' в текущем окне; счётчик обнулён.
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "ghost", TestIp));

        // when: 6-я неудача ghost при обнулённом снимком счётчике KDF.
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var blocked = await B10AuthRequests.LoginAsync(client, "ghost", "whatever1!");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 429; Δkdf=1 — столько же, сколько в ветке 429 известного логина
        // (TS-043): ветки неразличимы по стоимости (эталонная деривация).
        await ApiAssert.AssertMessageAsync(
            blocked, HttpStatusCode.TooManyRequests, "Слишком много попыток. Повторите позже");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "ghost", TestIp));
    }
}
