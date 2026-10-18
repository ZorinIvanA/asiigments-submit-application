using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-039 «Регистрация: границы длин login/email/fullName» (FR-006, глоссарий
/// «Тексты ошибок полей»; P1): given — анонимный доступ к POST /auth/register;
/// подготовлены 6 запросов с валидным форматом и уникальными свободными значениями:
/// login 100/101 (charset валиден), email 254/255 (формат валиден), fullName 200/201;
/// password/repeatPassword валидны и совпадают; when — последовательные POST
/// /auth/register с граничными значениями; then — login=100 — 201; login=101 — 400
/// errors.login=['Логин — от 1 до 100 символов']; email=254 — 201; email=255 — 400
/// errors.email=['Email — не более 254 символов']; fullName=200 — 201; fullName=201 —
/// 400 errors.fullName=['ФИО — от 1 до 200 символов'] (границы 1–100, 1–254, 1–200
/// включительно). Каждый запрос — со своим RemoteIpAddress: регистрационный лимитер
/// FR-004 (5 попыток/час на IP, считаются ВСЕ попытки) не является предметом кейса.
/// </summary>
public sealed class Ts039_FieldLengthBoundariesTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts039_FieldLengthBoundariesTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_LengthBoundaries_Login100Email254FullName200_Accepted_Overhead_Rejected()
    {
        // Граничные значения: charset/формат валидны, длина — ровно на границе и на 1 больше.
        var login100 = new string('a', 100);
        var login101 = new string('b', 101);
        var email254 = new string('c', 242) + "@example.com";
        var email255 = new string('d', 243) + "@example.com";
        var fullName200 = new string('Ф', 200);
        var fullName201 = new string('Х', 201);
        Assert.Equal(100, login100.Length);
        Assert.Equal(101, login101.Length);
        Assert.Equal(254, email254.Length);
        Assert.Equal(255, email255.Length);
        Assert.Equal(200, fullName200.Length);
        Assert.Equal(201, fullName201.Length);

        // when/then: login=100 — 201.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.1"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Логина Сто",
            login: login100,
            email: "b03-login100@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (login = 100 символов)");
        }

        // when/then: login=101 — 400, errors.login — только текст длины.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.2"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Логина Сто Один",
            login: login101,
            email: "b03-login101@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (login = 101 символ)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "login", "Логин — от 1 до 100 символов");
        }

        // when/then: email=254 — 201.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.3"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Эмеил Двести Пятьдесят Четыре",
            login: "b03-email254",
            email: email254,
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (email = 254 символа)");
        }

        // when/then: email=255 — 400, errors.email — только текст длины.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.4"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Граница Эмеил Двести Пятьдесят Пять",
            login: "b03-email255",
            email: email255,
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (email = 255 символов)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "email", "Email — не более 254 символов");
        }

        // when/then: fullName=200 — 201.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.5"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: fullName200,
            login: "b03-fullname200",
            email: "b03-fullname200@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        {
            await ResponseAssert.ParseWithStatusAsync(
                response, HttpStatusCode.Created, "POST /api/v1/auth/register (fullName = 200 символов)");
        }

        // when/then: fullName=201 — 400, errors.fullName — только текст длины.
        using (var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.39.0.6"))
        using (var response = await B03RegisterApi.PostAsync(
            client,
            fullName: fullName201,
            login: "b03-fullname201",
            email: "b03-fullname201@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!"))
        using (var body = await ResponseAssert.ParseWithStatusAsync(
            response, HttpStatusCode.BadRequest, "POST /api/v1/auth/register (fullName = 201 символ)"))
        {
            ResponseAssert.FieldErrorsExactly(body.RootElement, "fullName", "ФИО — от 1 до 200 символов");
        }
    }
}
