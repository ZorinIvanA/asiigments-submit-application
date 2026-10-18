using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-049 (P0, negative; FR-007, FR-023) «Вход: синтаксически некорректный
/// JSON — 400 без KDF».
/// given: Любое состояние хранилища (свежий хост); счётчик KDF обнулён.
/// when:  POST /auth/login с телом '{bad json'.
/// then:  400 'Данные заполнены неверно'; Δkdf=0 (FR-007 AC «Некорректный
///        JSON»). Тело ровно {message} — errors допустим только у полевой
///        валидации (IF-001).
/// </summary>
public sealed class Ts049_LoginMalformedJson400NoKdfTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS049_Login_WithMalformedJsonBody_Returns400WithoutAnyKdf()
    {
        // given: любое состояние хранилища (свежий хост); счётчик KDF обнулён
        // снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        // when: POST /auth/login с телом '{bad json'.
        var before = kdf.Snapshot();
        using var response = await HostClients.PostJsonAsync(client, HostClients.LoginPath, "{bad json");
        var after = kdf.Snapshot();

        // then: 400 'Данные заполнены неверно'; тело ровно {message}; Δkdf=0 —
        // ошибка формата не выполняет дериваций.
        var body = await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.BadRequest,
            "Данные заполнены неверно",
            exactSingleMessageProperty: true);
        ApiAssert.HasExactlyProperties(body, "message");
        Assert.Equal(0L, B11Kdf.TotalDelta(before, after));
    }
}
