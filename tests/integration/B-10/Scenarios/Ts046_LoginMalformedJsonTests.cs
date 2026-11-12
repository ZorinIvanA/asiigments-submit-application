using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-046 (P0, negative; FR-007, FR-023) «Вход: синтаксически некорректный JSON —
/// 400 без KDF».
/// given: Любое состояние хранилища (свежий хост); счётчик KDF сброшен.
/// when:  POST /auth/login с телом '{bad json'.
/// then:  400 'Данные заполнены неверно'; Δkdf=0. FR-007 AC «Некорректный JSON».
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts046_LoginMalformedJsonTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.46";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS046_Login_MalformedJson_Returns400WithoutAnyKdf()
    {
        // given: свежий хост (любое состояние хранилища); счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B10AuthGates.KdfSnapshot(_factory);
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when: POST /auth/login с телом '{bad json'.
        using var response = await B10AuthRequests.PostJsonAsync(client, B10CookieFlow.LoginPath, "{bad json");
        var after = B10AuthGates.KdfSnapshot(_factory);

        // then: 400 'Данные заполнены неверно'; Δkdf=0 — отказ зависит только от
        // формата тела, деривация не выполняется.
        await ApiAssert.AssertMessageAsync(
            response, HttpStatusCode.BadRequest, "Данные заполнены неверно");
        Assert.True(
            B10AuthGates.DeltaTotal(before, after) == 0,
            $"Ожидался Δkdf=0, фактически +{B10AuthGates.DeltaTotal(before, after)} " +
            $"(разбивка: {B10AuthGates.Breakdown(before, after)}).");
    }
}
