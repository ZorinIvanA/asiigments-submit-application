using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-043 (P0, negative; FR-007, FR-027) «Вход: 429 на неизвестном логине — та же
/// стоимость, что на известном».
/// given: 5 меток 'ghost|IP' в текущем окне; счётчик KDF сброшен.
/// when:  POST {login:'ghost', password:'whatever1!'}.
/// then:  429; Δkdf=1 — столько же дериваций, сколько в ветке 429 на известном
///        логине (TS-042): равномерная стоимость веток. FR-007 AC «429 на
///        неизвестном логине — KDF выполнен».
/// </summary>
public sealed class Ts043_Login429UnknownLoginEqualCostTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.43";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS043_Login_429OnUnknownLogin_CostsSameOneKdfAsKnownLogin()
    {
        // given: 5 меток 'ghost|IP' в текущем окне; счётчик KDF сброшен.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        for (var i = 0; i < 5; i++)
        {
            using var seed = await B09AuthHttp.LoginAsync(client, "ghost", "whatever1!");
            Assert.Equal(HttpStatusCode.Unauthorized, seed.StatusCode);
        }

        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "ghost", TestIp));

        // when: 6-я неудача ghost при обнулённом снимком счётчике KDF.
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var blocked = await B09AuthHttp.LoginAsync(client, "ghost", "whatever1!");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 429; Δkdf=1 — столько же, сколько в 429-ветке известного логина
        // (TS-042): ветки неразличимы по стоимости.
        var body = await B09Assertions.ParseObjectAsync(blocked, HttpStatusCode.TooManyRequests, "TS-043: вход ghost");
        B09Assertions.MessageIs(body, "Слишком много попыток. Повторите позже");
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(before, after));
        Assert.Equal(5, B09LoginMarkStore.MarksCount(_factory, "ghost", TestIp));
    }
}
