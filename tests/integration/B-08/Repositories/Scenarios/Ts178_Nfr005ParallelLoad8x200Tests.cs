using System.Collections.Concurrent;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Repositories.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Repositories.Scenarios;

/// <summary>
/// TS-178 «NFR-005: параллельная нагрузка 8×200 без исключений и без
/// нарушения инвариантов» (concurrency, NFR-005, FR-024, P1).
///
/// given: in-memory репозитории; 8 потоков со стартовой барьерой, сессия
///        teacher у каждого (клиент на поток); план смешанных операций —
///        25 итераций × 8 операций = 200 операций на поток:
///         1) POST /labs          — создание lab (пара (6, t·25+i+1) уникальна);
///         2) GET /labs/{id}      — чтение lab;
///         3) PUT /labs/{id}      — обновление lab;
///         4) POST /groups        — создание group (имя уникально по потоку);
///         5) GET /groups         — чтение groups;
///         6) DI-сид студента     — создание student (IUserRepository.Add,
///            атомарная ci-проверка login/email; ADR-016: обход
///            регистрационного лимита 5/час на IP; PBKDF2-хэш сид-метки —
///            один на фикстуру);
///         7) PUT /students/{id}/group — обновление student (назначение группы);
///         8) PUT /submissions    — создание/обновление submission (пара
///            (studentId, labId) уникальна).
/// when:  параллельное выполнение всех потоков.
/// then:  ноль исключений (ни одного выброшенного исключения и ни одного
///        статуса вне 2xx у плановых операций); после прогона инварианты
///        уникальности не нарушены: (semester,number) у lab, (studentId,labId)
///        у submission, lower(login)/lower(email) у пользователей и
///        lower(name) у групп (NFR-005).
/// </summary>
public sealed class Ts178_Nfr005ParallelLoad8x200Tests : IClassFixture<B08RepositoriesWebAppFactory>
{
    private const int ThreadCount = 8;
    private const int IterationsPerThread = 25;
    private const int OperationsPerIteration = 8;
    private const int LoadSemester = 6;

    private readonly B08RepositoriesWebAppFactory _factory;
    private readonly ConcurrentBag<string> _failures = new();

    public Ts178_Nfr005ParallelLoad8x200Tests(B08RepositoriesWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelMixedLoad_CompletesWithoutExceptionsAndInvariantsHold()
    {
        // given: in-memory репозитории; один сид-хэш пароля (метка seed) —
        // создание студентов идёт напрямую в хранилище (ADR-016).
        var passwordHash = _factory.Services.GetRequiredService<IPasswordHasher>()
            .Hash("Passw0rd!", KdfCallers.Seed);
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var clock = _factory.Services.GetRequiredService<TimeProvider>();

        // given: 8 сессий teacher (клиент и cookie-контейнер на поток).
        var clients = new List<B08RepositoriesClient>();
        for (var thread = 0; thread < ThreadCount; thread++)
        {
            clients.Add(await B08RepositoriesClient.LoginAsTeacherAsync(_factory));
        }

        // when: 8 потоков со стартовой барьерой выполняют план 8×25×200.
        using var barrier = new Barrier(participantCount: ThreadCount);
        var workers = clients
            .Select((client, thread) => Task.Run(() => RunThreadPlanAsync(
                client, thread, users, passwordHash, clock, barrier)))
            .ToArray();
        await Task.WhenAll(workers);
        foreach (var client in clients)
        {
            client.Dispose();
        }

        // then: ноль исключений — ни одного неожиданного статуса/выброса.
        Assert.True(
            _failures.IsEmpty,
            "NFR-005 нарушен: при параллельной нагрузке 8×200 были исключения/сбои операций:\n"
            + string.Join("\n", _failures.OrderBy(entry => entry, StringComparer.Ordinal)));

        // then: инвариант (semester, number) у lab.
        var labs = _factory.Services.GetRequiredService<ILabRepository>().ListByFilter(null);
        var duplicateLabPairs = labs
            .GroupBy(lab => (lab.Semester, lab.Number))
            .Where(group => group.Count() > 1)
            .Select(group => $"(semester={group.Key.Semester}, number={group.Key.Number})×{group.Count()}")
            .ToList();
        Assert.True(
            duplicateLabPairs.Count == 0,
            "NFR-005 нарушен: дубликаты пары (semester, number) у lab:\n" + string.Join("\n", duplicateLabPairs));

        // then: инвариант (studentId, labId) у submission.
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>()
            .ListByLabIds(labs.Select(lab => lab.Id).ToArray());
        var duplicateSubmissions = submissions
            .GroupBy(submission => (submission.StudentId, submission.LabId))
            .Where(group => group.Count() > 1)
            .Select(group => $"(studentId={group.Key.StudentId}, labId={group.Key.LabId})×{group.Count()}")
            .ToList();
        Assert.True(
            duplicateSubmissions.Count == 0,
            "NFR-005 нарушен: дубликаты пары (studentId, labId) у submission:\n"
            + string.Join("\n", duplicateSubmissions));

        // then: инварианты lower(login)/lower(email) у пользователей.
        var students = _factory.Services.GetRequiredService<IUserRepository>().ListStudents(null, null);
        AssertNoDuplicates(
            students, student => student.Login.Trim().ToLowerInvariant(), "lower(login) у пользователя");
        AssertNoDuplicates(
            students, student => student.Email.Trim().ToLowerInvariant(), "lower(email) у пользователя");

        // then: инвариант lower(name) у групп (ci-правило коллации ADR-013).
        var groups = _factory.Services.GetRequiredService<IGroupRepository>().List();
        AssertNoDuplicates(
            groups, group => group.Name.Trim().ToLowerInvariant(), "lower(name) у группы");
    }

    /// <summary>План одного потока: 25 итераций × 8 смешанных операций.</summary>
    private async Task RunThreadPlanAsync(
        B08RepositoriesClient client,
        int thread,
        IUserRepository users,
        string passwordHash,
        TimeProvider clock,
        Barrier barrier)
    {
        try
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(60));

            for (var iteration = 0; iteration < IterationsPerThread; iteration++)
            {
                var labNumber = thread * IterationsPerThread + iteration + 1;
                var groupNumber = thread * IterationsPerThread + iteration + 1;

                // 1. создание lab — пара (semester, number) уникальна по плану.
                var labId = await CreateLabAsync(client, thread, iteration, labNumber);
                if (labId is null)
                {
                    continue;
                }

                // 2. чтение lab.
                await ExpectAsync(
                    $"поток {thread}, итерация {iteration}, op2 GET /labs/{labId}",
                    () => client.GetAsync($"{B08RepositoriesClient.LabsEndpoint}/{labId}"),
                    HttpStatusCode.OK);

                // 3. обновление lab.
                await ExpectAsync(
                    $"поток {thread}, итерация {iteration}, op3 PUT /labs/{labId}",
                    () => client.PutJsonAsync(
                        $"{B08RepositoriesClient.LabsEndpoint}/{labId}",
                        $"{{\"number\":{labNumber},\"semester\":{LoadSemester}," +
                        "\"content\":" + B08RepositoriesClient.JsonString($"Нагрузка T{thread}-{iteration} обновлена") +
                        ",\"assignmentUrl\":null,\"defenseRequired\":false}"),
                    HttpStatusCode.OK);

                // 4. создание group — имя уникально по плану.
                var groupId = await CreateGroupAsync(client, thread, iteration, groupNumber);
                if (groupId is null)
                {
                    continue;
                }

                // 5. чтение groups.
                await ExpectAsync(
                    $"поток {thread}, итерация {iteration}, op5 GET /groups",
                    () => client.GetAsync(B08RepositoriesClient.GroupsEndpoint),
                    HttpStatusCode.OK);

                // 6. создание student (DI-сид, атомарная ci-проверка под нагрузкой).
                var studentId = Guid.NewGuid();
                var failure = RecordUnless(
                    $"поток {thread}, итерация {iteration}, op6 DI-сид студента loadstu-t{thread}-{iteration}",
                    () => users.Add(new User
                    {
                        Id = studentId,
                        Login = $"loadstu-t{thread}-{iteration}",
                        Email = $"loadstu-t{thread}-{iteration}@load.example.com",
                        PasswordHash = passwordHash,
                        FullName = $"Нагрузка Тест T{thread}-{iteration}",
                        Role = UserRoles.Student,
                        GroupId = null,
                        CreatedAt = clock.GetUtcNow().UtcDateTime,
                    }));
                if (failure is not null)
                {
                    continue;
                }

                // 7. обновление student — назначение группы.
                await ExpectAsync(
                    $"поток {thread}, итерация {iteration}, op7 PUT /students/{studentId}/group",
                    () => client.PutJsonAsync(
                        $"{B08RepositoriesClient.StudentsEndpoint}/{studentId}/group",
                        "{\"groupId\":" + B08RepositoriesClient.JsonString(groupId) + "}"),
                    HttpStatusCode.NoContent);

                // 8. создание/обновление submission — пара (studentId, labId).
                await ExpectAsync(
                    $"поток {thread}, итерация {iteration}, op8 PUT /submissions",
                    () => client.PutJsonAsync(
                        B08RepositoriesClient.SubmissionsEndpoint,
                        "{\"studentId\":" + B08RepositoriesClient.JsonString(studentId.ToString("D")) +
                        ",\"labId\":" + B08RepositoriesClient.JsonString(labId) +
                        ",\"submitDate\":\"2026-10-10\",\"defenseDate\":null}"),
                    HttpStatusCode.OK);
            }
        }
        catch (Exception exception)
        {
            _failures.Add($"поток {thread}: необработанное исключение плана: {exception}");
        }
    }

    /// <summary>op1: POST /labs; возвращает id созданной работы либо null (сбой записан).</summary>
    private async Task<string?> CreateLabAsync(B08RepositoriesClient client, int thread, int iteration, int labNumber)
    {
        string? labId = null;
        await ExpectAsync(
            $"поток {thread}, итерация {iteration}, op1 POST /labs (номер {labNumber})",
            () => client.PostJsonAsync(
                B08RepositoriesClient.LabsEndpoint,
                $"{{\"number\":{labNumber},\"semester\":{LoadSemester}," +
                "\"content\":" + B08RepositoriesClient.JsonString($"Нагрузочная работа T{thread}-{iteration}") +
                ",\"assignmentUrl\":null,\"defenseRequired\":false}"),
            HttpStatusCode.Created,
            body => labId = body.GetProperty("id") is { ValueKind: JsonValueKind.String } id
                ? id.GetString()
                : null);
        return labId;
    }

    /// <summary>op4: POST /groups; возвращает id созданной группы либо null (сбой записан).</summary>
    private async Task<string?> CreateGroupAsync(B08RepositoriesClient client, int thread, int iteration, int groupNumber)
    {
        string? groupId = null;
        await ExpectAsync(
            $"поток {thread}, итерация {iteration}, op4 POST /groups (номер {groupNumber})",
            () => client.PostJsonAsync(
                B08RepositoriesClient.GroupsEndpoint,
                "{\"name\":" + B08RepositoriesClient.JsonString($"Нагрузка-T{thread}-{iteration}") + "}"),
            HttpStatusCode.Created,
            body => groupId = body.GetProperty("id") is { ValueKind: JsonValueKind.String } id
                ? id.GetString()
                : null);
        return groupId;
    }

    /// <summary>
    /// Выполняет плановую операцию: ожидаемый статус — 2xx; отклонение
    /// (включая выброшенное исключение) записывается в журнал сбоев.
    /// </summary>
    private async Task ExpectAsync(
        string operation,
        Func<Task<HttpResponseMessage>> send,
        HttpStatusCode expected,
        Action<JsonElement>? inspectBody = null)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await send();
            if (response.StatusCode != expected)
            {
                _failures.Add($"{operation}: ожидался {expected}, фактически {response.StatusCode}: "
                              + Truncate(await B08RepositoriesClient.ReadBodyAsync(response)));
                return;
            }

            if (inspectBody is not null)
            {
                using var document = await B08RepositoriesClient.ReadJsonAsync(response);
                inspectBody(document.RootElement);
            }
        }
        catch (Exception exception)
        {
            _failures.Add($"{operation}: исключение: {exception}");
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Выполняет DI-операцию; выброшенное исключение записывается в журнал сбоев.</summary>
    private string? RecordUnless(string operation, Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            _failures.Add($"{operation}: исключение: {exception}");
            return exception.Message;
        }
    }

    /// <summary>Инвариант уникальности: ни один ci-ключ не встречается дважды.</summary>
    private static void AssertNoDuplicates<T>(
        IReadOnlyList<T> records,
        Func<T, string> key,
        string invariantName)
    {
        var duplicates = records
            .GroupBy(key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"«{group.Key}»×{group.Count()}")
            .ToList();
        Assert.True(
            duplicates.Count == 0,
            $"NFR-005 нарушен: дубликаты {invariantName}:\n" + string.Join("\n", duplicates));
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
