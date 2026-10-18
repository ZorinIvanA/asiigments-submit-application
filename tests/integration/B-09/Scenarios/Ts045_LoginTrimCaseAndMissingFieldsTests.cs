using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

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
public sealed class Ts045_LoginTrimCaseAndMissingFieldsTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.45";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS045_Login_TrimsCaseInsensitiveLogin_MissingFieldsGiveUnified401WithOneKdfEach()
    {
        // given: teacher/teacher123! существует (сид); меток лимитера нет (свежий хост).
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when (1): {login:'  TEACHER  ', password:'teacher123!'} — трим + регистр.
        var beforeTrim = B09KdfSeams.KdfSnapshot(_factory);
        using var trimmed = await B09AuthHttp.LoginAsync(client, "  TEACHER  ", B09AuthHttp.TeacherPassword);
        var afterTrim = B09KdfSeams.KdfSnapshot(_factory);

        // then (1): 200 MeDto (поиск по lower(trim(login))).
        var body = await B09Assertions.ParseObjectAsync(trimmed, HttpStatusCode.OK, "TS-045: вход '  TEACHER  '");
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(beforeTrim, afterTrim));

        // when (2): POST {} — оба поля отсутствуют; счётчик KDF сброшен снимком.
        var beforeEmpty = B09KdfSeams.KdfSnapshot(_factory);
        using var empty = await B09AuthHttp.PostJsonAsync(client, B09AuthHttp.LoginPath, "{}");
        var afterEmpty = B09KdfSeams.KdfSnapshot(_factory);

        // then (2): 401 'Неверный логин или пароль'; Δkdf=1 (равномерная ветка).
        var emptyBody = await B09Assertions.ParseObjectAsync(empty, HttpStatusCode.Unauthorized, "TS-045: тело {}");
        B09Assertions.MessageIs(emptyBody, "Неверный логин или пароль");
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(beforeEmpty, afterEmpty));

        // when (3): POST {login:'teacher'} без password; счётчик KDF сброшен снимком.
        var beforeNoPassword = B09KdfSeams.KdfSnapshot(_factory);
        using var noPassword = await B09AuthHttp.PostJsonAsync(
            client, B09AuthHttp.LoginPath, """{"login":"teacher"}""");
        var afterNoPassword = B09KdfSeams.KdfSnapshot(_factory);

        // then (3): 401 'Неверный логин или пароль'; Δkdf=1.
        var noPasswordBody = await B09Assertions.ParseObjectAsync(
            noPassword, HttpStatusCode.Unauthorized, "TS-045: тело без password");
        B09Assertions.MessageIs(noPasswordBody, "Неверный логин или пароль");
        Assert.Equal(1L, B09KdfSeams.DeltaTotal(beforeNoPassword, afterNoPassword));
    }
}
