using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-047 (P0, negative; FR-007) «Вход: отсутствующие поля — единый 401 с Δkdf=1»
/// (актуальная нумерация кейсов батча B-10; родственный тест предыдущей нумерации
/// — вторая часть Ts045_LoginTrimCaseAndMissingFieldsTests).
/// given: teacher существует; счётчик обнуляется перед каждым запросом (снимком).
/// when:  POST /auth/login с телом {}; затем с телом {login:'teacher'} (без
///        password).
/// then:  Оба — 401 'Неверный логин или пароль'; Δkdf=1 на каждый (равномерная
///        ветка: пустые значения не проходят проверку) (AC FR-007 «Отсутствующие
///        поля — единый 401»).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class B10Ts047_LoginMissingFieldsUnified401Tests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.47";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS047_Login_WithEmptyBodyAndBodyWithoutPassword_ReturnsUnified401WithOneKdfEach()
    {
        // given: teacher существует; счётчик обнуляется перед каждым запросом.
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when (1): POST /auth/login с телом {}; счётчик обнулён снимком.
        var beforeEmpty = B10AuthGates.KdfSnapshot(_factory);
        using var empty = await B10AuthRequests.PostJsonAsync(client, B10CookieFlow.LoginPath, "{}");
        var afterEmpty = B10AuthGates.KdfSnapshot(_factory);

        // then (1): 401 'Неверный логин или пароль'; Δkdf=1 (равномерная ветка:
        // пустые значения не проходят проверку).
        await ApiAssert.AssertMessageAsync(
            empty, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(beforeEmpty, afterEmpty) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(beforeEmpty, afterEmpty)} " +
            $"(разбивка: {B10AuthGates.Breakdown(beforeEmpty, afterEmpty)}).");

        // when (2): POST /auth/login с телом {login:'teacher'} (без password);
        // счётчик обнулён снимком.
        var beforeNoPassword = B10AuthGates.KdfSnapshot(_factory);
        using var noPassword = await B10AuthRequests.PostJsonAsync(
            client, B10CookieFlow.LoginPath, """{"login":"teacher"}""");
        var afterNoPassword = B10AuthGates.KdfSnapshot(_factory);

        // then (2): снова 401 'Неверный логин или пароль'; Δkdf=1 на каждый запрос.
        await ApiAssert.AssertMessageAsync(
            noPassword, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(beforeNoPassword, afterNoPassword) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(beforeNoPassword, afterNoPassword)} " +
            $"(разбивка: {B10AuthGates.Breakdown(beforeNoPassword, afterNoPassword)}).");
    }
}
