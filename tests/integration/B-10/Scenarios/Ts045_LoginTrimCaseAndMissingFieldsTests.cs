using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-045 (P1, boundary; FR-007) «Вход: трим и регистр логина; отсутствующие
/// поля — единый 401».
/// given: teacher/teacher123! существует; меток лимитера нет; счётчик KDF
///        сбрасывается перед каждым запросом (снимком).
/// when:  POST {login:'  TEACHER  ', password:'teacher123!'}; отдельно POST {} и
///        POST {login:'teacher'} без password.
/// then:  Первый — 200 MeDto; остальные — 401 'Неверный логин или пароль' с
///        Δkdf=1 на каждый (равномерная ветка: пустые значения не проходят
///        проверку). FR-007 AC «Логин без учёта регистра и с пробелами»,
///        «Отсутствующие поля — единый 401».
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts045_LoginTrimCaseAndMissingFieldsTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.10.45";

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS045_Login_TrimsCaseInsensitiveLogin_MissingFieldsGiveUnified401WithOneKdfEach()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий хост).
        _ = _factory.Services;
        using var client = B10AuthRequests.Create(_factory, TestIp);

        // when (1): {login:'  TEACHER  ', password:'teacher123!'} — трим + регистр;
        // счётчик KDF сброшен снимком.
        var beforeTrim = B10AuthGates.KdfSnapshot(_factory);
        using var trimmed = await B10AuthRequests.LoginAsync(client, "  TEACHER  ", B10AuthRequests.TeacherPassword);
        var afterTrim = B10AuthGates.KdfSnapshot(_factory);

        // then (1): 200 MeDto (поиск по lower(trim(login))); Δkdf=1.
        var body = await ApiAssert.ReadOkJsonAsync(trimmed);
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.True(
            B10AuthGates.DeltaTotal(beforeTrim, afterTrim) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(beforeTrim, afterTrim)} " +
            $"(разбивка: {B10AuthGates.Breakdown(beforeTrim, afterTrim)}).");

        // when (2): POST {} — оба поля отсутствуют; счётчик KDF сброшен снимком.
        var beforeEmpty = B10AuthGates.KdfSnapshot(_factory);
        using var empty = await B10AuthRequests.PostJsonAsync(client, B10CookieFlow.LoginPath, "{}");
        var afterEmpty = B10AuthGates.KdfSnapshot(_factory);

        // then (2): 401 'Неверный логин или пароль'; Δkdf=1 (равномерная ветка).
        await ApiAssert.AssertMessageAsync(
            empty, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(beforeEmpty, afterEmpty) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(beforeEmpty, afterEmpty)} " +
            $"(разбивка: {B10AuthGates.Breakdown(beforeEmpty, afterEmpty)}).");

        // when (3): POST {login:'teacher'} без password; счётчик KDF сброшен снимком.
        var beforeNoPassword = B10AuthGates.KdfSnapshot(_factory);
        using var noPassword = await B10AuthRequests.PostJsonAsync(
            client, B10CookieFlow.LoginPath, """{"login":"teacher"}""");
        var afterNoPassword = B10AuthGates.KdfSnapshot(_factory);

        // then (3): 401 'Неверный логин или пароль'; Δkdf=1.
        await ApiAssert.AssertMessageAsync(
            noPassword, HttpStatusCode.Unauthorized, "Неверный логин или пароль");
        Assert.True(
            B10AuthGates.DeltaTotal(beforeNoPassword, afterNoPassword) == 1,
            $"Ожидался Δkdf=1, фактически +{B10AuthGates.DeltaTotal(beforeNoPassword, afterNoPassword)} " +
            $"(разбивка: {B10AuthGates.Breakdown(beforeNoPassword, afterNoPassword)}).");
    }
}
