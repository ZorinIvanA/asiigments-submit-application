using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-043 (P0, negative; FR-007, FR-027) «Вход: 429 на неизвестном логине — та же
/// стоимость, что на известном».
/// given: 5 меток 'ghost|IP' в текущем окне; счётчик KDF сброшен.
/// when:  POST {login:'ghost', password:'whatever1!'}.
/// then:  429; Δkdf=1 — столько же дериваций, сколько в ветке 429 на известном
///        логине (TS-042): равномерная стоимость веток. FR-007 AC «429 на
///        неизвестном логине — KDF выполнен».
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts043_Login429UnknownLoginEqualCostTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.43";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS043_Login_429OnUnknownLogin_CostsSameOneKdfAsKnownLogin()
    {
        // given: 5 меток 'ghost|IP' в текущем окне; счётчик KDF сброшен.
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

        // then: 429; Δkdf=1 — столько же, сколько в 429-ветке известного логина
        // (TS-042): ветки неразличимы по стоимости; число меток осталось 5.
        await ApiAssert.AssertMessageAsync(
            blocked, HttpStatusCode.TooManyRequests, "Слишком много попыток. Повторите позже");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
        Assert.Equal(5, B10AuthGates.LoginMarksCount(_factory, "ghost", TestIp));
    }
}
