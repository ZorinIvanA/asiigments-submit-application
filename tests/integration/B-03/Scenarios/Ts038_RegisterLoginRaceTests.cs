using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B03.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-038 «Регистрация: атомарность уникальности login и email при параллельных
/// запросах» (FR-006, FR-024 AC «Атомарность уникальности»; P1). Гонки разведены
/// по ключам, чтобы конфликт login не маскировал конфликт email и наоборот:
/// в ветке (1) email-адреса разные, в ветке (2) логины разные.
/// given — свободны логины 'racelogin1' (lower — ЕДИНСТВЕННЫЙ оспариваемый ключ
/// ветки (1)), 'racemail1', 'racemail2' (lower) и email race1a@example.com,
/// race1b@example.com, shared.race@example.com (lower; в ветке (2) предъявляется
/// регистровыми вариантами 'Shared.Race@example.com' и 'shared.race@example.com');
/// регистрационный лимит не исчерпан (4 запроса с одного IP ≤ 5/3600с; при
/// повторных прогонах счётчики политики register сбрасываются тестовым швом
/// IRateLimitStore — СКЕПТИК-ISS-001: пара ветки (1) — регистровые варианты
/// ОДНОГО lower(login)='racelogin1', иначе конфликт 409 недостижим);
/// when — (1) два параллельных POST /api/v1/auth/register с регистровыми
/// вариантами одного lower(login)='racelogin1' и разными email (барьерный старт);
/// (2) два параллельных POST с регистровыми вариантами одного
/// lower(email)='shared.race@example.com' и разными логинами (барьерный старт);
/// then — (1) ровно один 201, второй 409 'Пользователь с таким логином уже
/// существует'; (2) ровно один 201, второй 409 'Пользователь с таким email уже
/// существует'; после обеих гонок в хранилище ровно один пользователь с
/// lower(login)='racelogin1' и ровно один с lower(email)='shared.race@example.com'
/// — дублей нет.
/// </summary>
public sealed class Ts038_RegisterLoginRaceTests : IClassFixture<B03HostFactory>
{
    /// <summary>Единый IP всех четырёх запросов кейса (4 ≤ лимит 5/3600с, FR-004).</summary>
    private const string CaseRemoteIp = "10.38.0.1";

    private readonly B03HostFactory _factory;

    public Ts038_RegisterLoginRaceTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ParallelSameLogin_And_ParallelSameEmail_ExactlyOneCreatedEach_NoDuplicates()
    {
        // given: все оспариваемые ключи свободны; счётчики политики register сброшены
        // тестовым швом IRateLimitStore (повторные прогоны детерминированы).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        Assert.Null(B03UserSeed.FindByLogin(_factory, "racelogin1"));
        Assert.Null(B03UserSeed.FindByLogin(_factory, "racemail1"));
        Assert.Null(B03UserSeed.FindByLogin(_factory, "racemail2"));
        Assert.Null(users.GetByEmail("race1a@example.com"));
        Assert.Null(users.GetByEmail("race1b@example.com"));
        Assert.Null(users.GetByEmail("shared.race@example.com"));
        ResetRegisterPolicy();
        var studentsBefore = B03UserSeed.CountStudents(_factory);

        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: CaseRemoteIp);

        // when (1): два параллельных POST с регистровыми вариантами одного
        // lower(login)='racelogin1' и разными email (задачи стартуются до ожидания —
        // барьерный старт).
        var raceLoginFirst = B03RegisterApi.PostAsync(
            client,
            fullName: "Гонка Один",
            login: "RaceLogin1",
            email: "race1a@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var raceLoginSecond = B03RegisterApi.PostAsync(
            client,
            fullName: "Гонка Два",
            login: "racelogin1",
            email: "race1b@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var loginRace = await Task.WhenAll(raceLoginFirst, raceLoginSecond);

        // then (1): ровно один 201, второй — 409 CONFLICT_LOGIN (без иных статусов:
        // ни 429, ни 500).
        await AssertRaceOutcomeAsync(
            loginRace, HttpStatusCode.Conflict, "Пользователь с таким логином уже существует", "ветка (1): гонка login");

        // when (2): два параллельных POST с регистровыми вариантами одного
        // lower(email)='shared.race@example.com' и разными логинами (барьерный старт).
        var raceEmailFirst = B03RegisterApi.PostAsync(
            client,
            fullName: "Мыло Один",
            login: "racemail1",
            email: "Shared.Race@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var raceEmailSecond = B03RegisterApi.PostAsync(
            client,
            fullName: "Мыло Два",
            login: "racemail2",
            email: "shared.race@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");
        var emailRace = await Task.WhenAll(raceEmailFirst, raceEmailSecond);

        // then (2): ровно один 201, второй — 409 CONFLICT_EMAIL.
        await AssertRaceOutcomeAsync(
            emailRace, HttpStatusCode.Conflict, "Пользователь с таким email уже существует", "ветка (2): гонка email");

        // then: после обеих гонок дублей нет — состав вырос ровно на двух студентов
        // (по одному на гонку).
        Assert.Equal(studentsBefore + 2, B03UserSeed.CountStudents(_factory));

        // then: ровно один пользователь с lower(login)='racelogin1' — ci-индекс отдаёт
        // единственную запись, и создан ровно один из двух email-вариантов ветки (1).
        var loginRaceWinner = users.GetByLogin("racelogin1");
        Assert.NotNull(loginRaceWinner);
        var byRace1a = users.GetByEmail("race1a@example.com");
        var byRace1b = users.GetByEmail("race1b@example.com");
        Assert.True(
            (byRace1a is null) != (byRace1b is null),
            "Ожидался ровно один созданный пользователь из пары email race1a/race1b@example.com, фактически " +
            $"race1a={(byRace1a is null ? "нет" : byRace1a.Login)}, race1b={(byRace1b is null ? "нет" : byRace1b.Login)}");
        Assert.Equal(loginRaceWinner.Login, (byRace1a ?? byRace1b)!.Login);

        // then: ровно один пользователь с lower(email)='shared.race@example.com' —
        // ci-индекс отдаёт единственную запись с одним из двух логинов ветки (2),
        // второй логин свободен.
        var emailRaceWinner = users.GetByEmail("shared.race@example.com");
        Assert.NotNull(emailRaceWinner);
        Assert.True(
            emailRaceWinner.Login is "racemail1" or "racemail2",
            $"Ожидался победитель гонки email с логином 'racemail1'/'racemail2', фактически '{emailRaceWinner.Login}'");
        var loserLogin = emailRaceWinner.Login == "racemail1" ? "racemail2" : "racemail1";
        Assert.Null(B03UserSeed.FindByLogin(_factory, loserLogin));
    }

    /// <summary>
    /// Точная форма исхода одной гонки: ровно один 201 и ровно один ожидаемый
    /// конфликт с дословным текстом message (состав статусов исключает 429/500).
    /// </summary>
    private static async Task AssertRaceOutcomeAsync(
        HttpResponseMessage[] responses,
        HttpStatusCode conflictStatus,
        string conflictMessage,
        string raceLabel)
    {
        var statuses = responses.Select(response => response.StatusCode).ToArray();
        Assert.True(
            statuses.Count(status => status == HttpStatusCode.Created) == 1,
            $"{raceLabel}: ожидался ровно один 201, фактически [{string.Join(", ", statuses)}]");
        Assert.True(
            statuses.Count(status => status == conflictStatus) == 1,
            $"{raceLabel}: ожидался ровно один {(int)conflictStatus}, фактически [{string.Join(", ", statuses)}]");

        using var conflictBody = await ResponseAssert.ParseWithStatusAsync(
            responses.First(response => response.StatusCode == conflictStatus),
            conflictStatus,
            $"{raceLabel}: тело конфликтного ответа");
        ResponseAssert.MessageIs(conflictBody.RootElement, conflictMessage);
    }

    /// <summary>
    /// Сброс счётчиков регистрационного лимитера тестовым швом IRateLimitStore
    /// (given TS-038: «при повторных прогонах счётчики политики register
    /// сбрасываются»): полная вычистка ключей политики до запросов кейса.
    /// </summary>
    private void ResetRegisterPolicy()
    {
        var store = _factory.Services.GetRequiredService<IRateLimitStore>();
        foreach (var key in store.GetKeys(RateLimitPolicies.Register))
        {
            Assert.True(store.Remove(RateLimitPolicies.Register, key));
        }
    }
}
