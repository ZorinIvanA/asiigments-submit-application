using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-048 (P0, negative; FR-007) «Вход: отсутствующие поля — единый 401 с одной
/// деривацией».
/// given: teacher существует; счётчик KDF обнулён перед каждым запросом
///        (снимком).
/// when:  POST с телом {} и POST с телом {login:'teacher'} (без password).
/// then:  Оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос
///        (равномерная ветка: пустые значения не проходят проверку; FR-007 AC
///        «Отсутствующие поля»).
/// </summary>
public sealed class Ts048_LoginMissingFieldsUnified401OneKdfTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS048_Login_WithMissingFields_ReturnsUnified401WithOneKdfPerRequest()
    {
        // given: teacher существует (демо-сид); счётчик KDF обнуляется снимком
        // перед каждым запросом.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        using var client = HostClients.Create(_factory);

        // when (1): POST с телом {} — оба поля отсутствуют (lenient → '').
        var beforeEmpty = kdf.Snapshot();
        using var empty = await HostClients.PostJsonAsync(client, HostClients.LoginPath, "{}");
        var afterEmpty = kdf.Snapshot();

        // then (1): 401 'Неверный логин или пароль'; Δkdf=1.
        await ApiAssert.AssertMessageAsync(
            empty,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(beforeEmpty, afterEmpty));

        // when (2): POST с телом {login:'teacher'} — без password.
        var beforeNoPassword = kdf.Snapshot();
        using var noPassword = await HostClients.PostJsonAsync(
            client,
            HostClients.LoginPath,
            """{"login":"teacher"}""");
        var afterNoPassword = kdf.Snapshot();

        // then (2): 401 'Неверный логин или пароль' (тот же единый текст);
        // Δkdf=1 на этот запрос тоже.
        await ApiAssert.AssertMessageAsync(
            noPassword,
            HttpStatusCode.Unauthorized,
            "Неверный логин или пароль",
            exactSingleMessageProperty: true);
        Assert.Equal(1L, B11Kdf.TotalDelta(beforeNoPassword, afterNoPassword));
    }
}
