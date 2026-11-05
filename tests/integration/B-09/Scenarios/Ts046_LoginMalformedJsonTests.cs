using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-046 (P0, negative; FR-007, FR-023) «Вход: синтаксически некорректный JSON —
/// 400 без KDF».
/// given: Любое состояние хранилища (свежий хост); счётчик KDF сброшен.
/// when:  POST /auth/login с телом '{bad json'.
/// then:  400 'Данные заполнены неверно'; Δkdf=0. FR-007 AC «Некорректный JSON».
/// </summary>
public sealed class Ts046_LoginMalformedJsonTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.46";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS046_Login_MalformedJson_Returns400WithoutAnyKdf()
    {
        // given: свежий хост (любое состояние хранилища); счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: POST /auth/login с телом '{bad json'.
        using var response = await B09AuthHttp.PostJsonAsync(client, B09AuthHttp.LoginPath, "{bad json");
        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: 400 'Данные заполнены неверно'; Δkdf=0 — отказ зависит только от
        // формата тела, деривация не выполняется.
        var body = await B09Assertions.ParseObjectAsync(response, HttpStatusCode.BadRequest, "TS-046: битый JSON");
        B09Assertions.MessageIs(body, "Данные заполнены неверно");
        Assert.Equal(0L, B09KdfSeams.DeltaTotal(before, after));
    }
}
