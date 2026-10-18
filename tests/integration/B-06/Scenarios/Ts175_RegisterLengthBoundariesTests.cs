using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-175 «Регистрация: границы длин login 100/101, email 254/255, fullName
/// 200/201 с текстами словаря» (boundary, P1, FR-006/FR-023).
///
/// given: хост Development; регистрационный лимитер учитывает ЛЮБОЙ запрос
///        (TryAcquire выполняется ДО валидации, 5/3600с на IP), а кейс делает
///        ШЕСТЬ POST с одного IP — счётчики политики register сбрасываются перед
///        КАЖДЫМ запросом тестовым швом IRateLimitStore; в каждом запросе
///        варьируется ТОЛЬКО одно поле, прочие поля валидны и совпадают
///        (password='Passw0rd!', repeatPassword='Passw0rd!'); используемые
///        логины и email свободны (демо-сид логинов student01..32 и teacher
///        с кейсовыми не пересекается).
/// when:  шесть POST /api/v1/auth/register: (1) login='a'×100; (2) login='a'×101;
///        (3) email длиной ровно 254 символа (локальная часть 242 символа +
///        '@example.com'); (4) email ровно 255 (локальная часть 243);
///        (5) fullName='А'×200; (6) fullName='А'×201.
/// then:  (1)/(3)/(5) — 201, значения ровно на границах валидны (пользователь
///        создан); (2) — 400 «Данные заполнены неверно», errors.login ровно
///        ['Логин — от 1 до 100 символов']; (4) — 400, errors.email ровно
///        ['Email — не более 254 символов']; (6) — 400, errors.fullName ровно
///        ['ФИО — от 1 до 200 символов'] — тексты ДОСЛОВНО из словаря «Текстов
///        ошибок полей» (FR-023: тексты вне словаря в ответах API запрещены);
///        при каждом 400 пользователь не создан (login/email — после трима:
///        «1–100», «1–254», «1–200»).
///
/// Примечание зоны: текущая редакция кейса (переиздание a-132 по сверке
/// дерева) закрепляет зону-владельца tests/integration/B-06 — зона test_zone
/// батча B-06; прежнее закрепление префикса Ts175 за B-03 снято (файлов Ts175
/// в B-03 нет). Файл размещён в зоне батча, when/then исполнены дословно.
/// </summary>
public sealed class Ts175_RegisterLengthBoundariesTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string TestPassword = "Passw0rd!";

    /// <summary>Валидное ФИО запросов, где варьируется не оно (1–200 после трима).</summary>
    private const string SharedFullName = "Тест Граница";

    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts175_RegisterLengthBoundariesTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_LengthBoundaries_ExactValuesValid_OffByOneRejectedWithDictionaryTexts()
    {
        // given: используемые логины и email свободны.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(users.GetByLogin(new string('a', 100)));
        Assert.Null(users.GetByLogin(new string('a', 101)));
        Assert.Null(users.GetByLogin("ts175-email254"));
        Assert.Null(users.GetByLogin("ts175-email255"));
        Assert.Null(users.GetByLogin("ts175-full200"));
        Assert.Null(users.GetByLogin("ts175-full201"));

        // when (1): POST /auth/register с login ровно 100 символов ('a'×100,
        // charset [A-Za-z0-9._-] соблюдён); прочие поля валидны.
        using var login100 = await RegisterAsync(
            fullName: SharedFullName,
            login: new string('a', 100),
            email: "ts175-login100@example.com");

        // then (1): 201 — значение ровно на границе валидно; пользователь создан.
        Assert.Equal(HttpStatusCode.Created, login100.StatusCode);
        Assert.NotNull(users.GetByLogin(new string('a', 100)));

        // when (2): POST /auth/register с login 101 символ ('a'×101).
        using var login101 = await RegisterAsync(
            fullName: SharedFullName,
            login: new string('a', 101),
            email: "ts175-login101@example.com");

        // then (2): 400 «Данные заполнены неверно», errors.login ровно
        // ['Логин — от 1 до 100 символов']; пользователь не создан.
        Assert.Equal(HttpStatusCode.BadRequest, login101.StatusCode);
        var login101Body = await BodyAssertions.ReadRootObjectAsync(login101);
        BodyAssertions.MessageIs(login101Body, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(login101Body, "login", "Логин — от 1 до 100 символов");
        Assert.Null(users.GetByLogin(new string('a', 101)));
        Assert.Null(users.GetByEmail("ts175-login101@example.com"));

        // when (3): POST /auth/register с email длиной ровно 254 символа
        // (242 + '@example.com').
        var email254 = new string('a', 242) + "@example.com";
        Assert.Equal(254, email254.Length);
        using var email254Response = await RegisterAsync(
            fullName: SharedFullName,
            login: "ts175-email254",
            email: email254);

        // then (3): 201 — значение ровно на границе валидно; пользователь создан.
        Assert.Equal(HttpStatusCode.Created, email254Response.StatusCode);
        Assert.NotNull(users.GetByEmail(email254));

        // when (4): POST /auth/register с email длиной ровно 255 символов
        // (243 + '@example.com').
        var email255 = new string('a', 243) + "@example.com";
        Assert.Equal(255, email255.Length);
        using var email255Response = await RegisterAsync(
            fullName: SharedFullName,
            login: "ts175-email255",
            email: email255);

        // then (4): 400, errors.email ровно ['Email — не более 254 символов'];
        // пользователь не создан.
        Assert.Equal(HttpStatusCode.BadRequest, email255Response.StatusCode);
        var email255Body = await BodyAssertions.ReadRootObjectAsync(email255Response);
        BodyAssertions.MessageIs(email255Body, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(email255Body, "email", "Email — не более 254 символов");
        Assert.Null(users.GetByEmail(email255));
        Assert.Null(users.GetByLogin("ts175-email255"));

        // when (5): POST /auth/register с fullName 'А'×200.
        using var fullName200 = await RegisterAsync(
            fullName: new string('А', 200),
            login: "ts175-full200",
            email: "ts175-full200@example.com");

        // then (5): 201 — значение ровно на границе валидно; пользователь создан.
        Assert.Equal(HttpStatusCode.Created, fullName200.StatusCode);
        Assert.NotNull(users.GetByLogin("ts175-full200"));

        // when (6): POST /auth/register с fullName 'А'×201.
        using var fullName201 = await RegisterAsync(
            fullName: new string('А', 201),
            login: "ts175-full201",
            email: "ts175-full201@example.com");

        // then (6): 400, errors.fullName ровно ['ФИО — от 1 до 200 символов'];
        // пользователь не создан.
        Assert.Equal(HttpStatusCode.BadRequest, fullName201.StatusCode);
        var fullName201Body = await BodyAssertions.ReadRootObjectAsync(fullName201);
        BodyAssertions.MessageIs(fullName201Body, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(fullName201Body, "fullName", "ФИО — от 1 до 200 символов");
        Assert.Null(users.GetByLogin("ts175-full201"));
        Assert.Null(users.GetByEmail("ts175-full201@example.com"));
    }

    /// <summary>
    /// POST /api/v1/auth/register кейса: перед КАЖДЫМ запросом счётчики политики
    /// register сбрасываются тестовым швом IRateLimitStore (given: шесть запросов
    /// одним IP превысили бы лимит 5/3600с — TryAcquire учитывает любой запрос).
    /// </summary>
    private Task<HttpResponseMessage> RegisterAsync(string fullName, string login, string email)
    {
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        foreach (var key in store.GetKeys(RateLimitPolicies.Register))
        {
            Assert.True(store.Remove(RateLimitPolicies.Register, key));
        }

        return ApiRequests.RegisterAsync(
            _factory.CreateClient(),
            fullName,
            login,
            email,
            TestPassword,
            repeatPassword: TestPassword);
    }
}
