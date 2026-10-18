using System.Globalization;
using System.Text.Json;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-138 «Сид: Development с демо-данными — полный демо-набор мок-слоя» (FR-025 AC
/// «Старт с демо-данными (Development)», P1; тип happy_path).
///
/// Единственный файл кейса по описи ревизии: прежняя внутризонная коллизия двух
/// файлов с префиксом Ts138 (Ts138_DemoDataMockParityTests.cs и
/// Ts138_DevelopmentDemoDataCountsTests.cs) устранена объединением в один —
/// второй файл выведен по описи. Один класс, два теста: (1) КОЛИЧЕСТВА — 3 группы,
/// 32 студента, 23 работы (20+3 по семестрам), 4 сдачи; (2) ДЕТАЛИ состава —
/// email/fullName/распределение по группам, признаки работ, даты сдач, updatedBy.
///
/// given: чистое хранилище; Development (Seed__DemoData не задан — умолчание true,
///        дефолтный сид-пароль допустим).
/// when:  старт приложения; вход teacher/teacher123!; подсчёт и инспекция состава
///        через API teacher (списки/ведомости с пагинацией).
/// then:  вход — 200; 3 группы ИК-221/ИК-222/ИК-223; 32 студента (email
///        studentNN@example.com, fullName 'Иванов Иван Иванович NN'; 01–25 в ИК-221,
///        26–30 в ИК-222, 31–32 без группы); 23 работы (сем.1 №1–20, сем.2 №1–3;
///        defenseRequired у чётных N; assignmentUrl
///        'https://git.example.com/assignments/&lt;sem&gt;/&lt;N&gt;' для N, кратных 5, иначе
///        null); 4 сдачи (student01: 1.1 2026-09-01/2026-09-11; 1.2 2026-09-02/
///        2026-09-12; 1.3 2026-09-03/null; student02: 1.1 2026-09-01/null),
///        updatedBy = uuid преподавателя.
/// </summary>
public sealed class Ts138_DemoDataMockParityTests
{
    private const string DefaultLabContentPrefix = "Содержание лабораторной работы №";
    private const string AssignmentUrlTemplate = "https://git.example.com/assignments/{0}/{1}";
    private const string StudentEmailTemplate = "student{0:00}@example.com";
    private const string StudentFullNameTemplate = "Иванов Иван Иванович {0:00}";

    /// <summary>Тест 1 — КОЛИЧЕСТВА демо-набора, видимые через API teacher.</summary>
    [Fact]
    public async Task DevelopmentStart_SeedsFullDemoSet_VisibleThroughTeacherApi()
    {
        // given: чистый Development-хост; Seed__DemoData не задан (умолчание true).
        using var factory = new B02WebAppFactory(Environments.Development, useHarnessDefaults: false);
        using var client = factory.CreateWarmClient();

        // when: вход teacher/teacher123!.
        using var login = await HostClients.LoginAsDefaultTeacherAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // then: 3 группы.
        var groups = await client.GetFromJsonAsync<JsonElement>("/api/v1/groups");
        Assert.Equal(3, ApiShapes.TotalOf(groups));

        // then: 32 студента.
        var students = await client.GetFromJsonAsync<JsonElement>("/api/v1/students");
        Assert.Equal(32, ApiShapes.TotalOf(students));

        // then: 23 работы; по семестрам — 20 и 3.
        var labsAll = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs");
        Assert.Equal(23, ApiShapes.TotalOf(labsAll));
        var labsSemester1 = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs?semester=1");
        Assert.Equal(20, ApiShapes.TotalOf(labsSemester1));
        var labsSemester2 = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs?semester=2");
        Assert.Equal(3, ApiShapes.TotalOf(labsSemester2));

        // then: 4 сдачи — все в ведомости ИК-221 за семестр 1; в остальных группах сдач нет.
        var ik221 = ApiShapes.FindGroupIdByName(groups, "ИК-221")
            ?? throw new InvalidOperationException("Группа ИК-221 не найдена в списке групп.");
        var ik222 = ApiShapes.FindGroupIdByName(groups, "ИК-222")
            ?? throw new InvalidOperationException("Группа ИК-222 не найдена в списке групп.");
        var ik223 = ApiShapes.FindGroupIdByName(groups, "ИК-223")
            ?? throw new InvalidOperationException("Группа ИК-223 не найдена в списке групп.");
        Assert.Equal(4, await CountRealSubmissionsAsync(client, ik221, semester: 1));
        Assert.Equal(0, await CountRealSubmissionsAsync(client, ik222, semester: 1));
        Assert.Equal(0, await CountRealSubmissionsAsync(client, ik223, semester: 1));

        // then (CR-003): в ведомостях семестра 2 реальных сдач нет — «ровно 4 сдачи»
        // проверяется суммарно по обоим семестрам, а не только семестром 1.
        Assert.Equal(0, await CountRealSubmissionsAsync(client, ik221, semester: 2));
        Assert.Equal(0, await CountRealSubmissionsAsync(client, ik222, semester: 2));
        Assert.Equal(0, await CountRealSubmissionsAsync(client, ik223, semester: 2));
    }

    /// <summary>Тест 2 — ДЕТАЛИ состава демо-набора (паритет с мок-слоем).</summary>
    [Fact]
    public async Task DevelopmentDemoData_MatchesMockLayerInDetails()
    {
        using var factory = new B02WebAppFactory(Environments.Development, useHarnessDefaults: false);
        using var client = factory.CreateWarmClient();
        using var login = await HostClients.LoginAsDefaultTeacherAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // --- Студенты: полный обход страниц списка. ---
        var studentsByLogin = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var studentPage = 1;
        while (true)
        {
            var payload = await client.GetFromJsonAsync<JsonElement>($"/api/v1/students?page={studentPage}");
            var items = ApiShapes.ItemsOf(payload);
            foreach (var student in items.EnumerateArray())
            {
                studentsByLogin[student.GetProperty("login").GetString()!] = student.Clone();
            }

            if (items.GetArrayLength() == 0)
            {
                break;
            }

            Assert.True(++studentPage <= 50, "Обход списка студентов не завершается.");
        }

        Assert.Equal(32, studentsByLogin.Count);
        for (var number = 1; number <= 32; number++)
        {
            var suffix = number.ToString("00", CultureInfo.InvariantCulture);
            var login_ = $"student{suffix}";
            Assert.True(studentsByLogin.TryGetValue(login_, out var student), $"Студент {login_} не найден.");
            Assert.Equal(
                string.Format(CultureInfo.InvariantCulture, StudentEmailTemplate, number),
                student.GetProperty("email").GetString());
            Assert.Equal(
                string.Format(CultureInfo.InvariantCulture, StudentFullNameTemplate, number),
                student.GetProperty("fullName").GetString());
            var expectedGroup = number <= 25 ? "ИК-221" : number <= 30 ? "ИК-222" : null;
            Assert.Equal(expectedGroup, student.GetProperty("groupName").GetString());
        }

        // --- Работы: полный обход страниц списка. ---
        var labsByPair = new Dictionary<(int Semester, int Number), JsonElement>();
        var labPage = 1;
        while (true)
        {
            var payload = await client.GetFromJsonAsync<JsonElement>($"/api/v1/labs?page={labPage}");
            var items = ApiShapes.ItemsOf(payload);
            foreach (var lab in items.EnumerateArray())
            {
                labsByPair[(lab.GetProperty("semester").GetInt32(), lab.GetProperty("number").GetInt32())] = lab.Clone();
            }

            if (items.GetArrayLength() == 0)
            {
                break;
            }

            Assert.True(++labPage <= 50, "Обход списка работ не завершается.");
        }

        Assert.Equal(23, labsByPair.Count);
        foreach (var (semester, lastNumber) in new[] { (1, 20), (2, 3) })
        {
            for (var number = 1; number <= lastNumber; number++)
            {
                Assert.True(
                    labsByPair.TryGetValue((semester, number), out var lab),
                    $"Работа {semester}:{number} не найдена.");
                Assert.Equal(
                    string.Create(CultureInfo.InvariantCulture, $"{DefaultLabContentPrefix}{number}"),
                    lab.GetProperty("content").GetString());
                Assert.Equal(number % 2 == 0, lab.GetProperty("defenseRequired").GetBoolean());
                var expectedUrl = number % 5 == 0
                    ? string.Format(CultureInfo.InvariantCulture, AssignmentUrlTemplate, semester, number)
                    : null;
                Assert.Equal(expectedUrl, lab.GetProperty("assignmentUrl").GetString());
            }
        }

        // --- Сдачи: ведомость ИК-221 за семестр 1, все страницы. ---
        var groups = await client.GetFromJsonAsync<JsonElement>("/api/v1/groups");
        var ik221 = ApiShapes.FindGroupIdByName(groups, "ИК-221")
            ?? throw new InvalidOperationException("Группа ИК-221 не найдена.");
        var actualSubmissions = new HashSet<(string Login, int Semester, int Number, string? Submit, string? Defense)>();
        var gridPage = 1;
        while (true)
        {
            var grid = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/submissions?semester=1&groupId={ik221}&page={gridPage}");
            foreach (var submission in grid.GetProperty("submissions").EnumerateArray())
            {
                if (!ApiShapes.IsRealSubmission(submission))
                {
                    continue;
                }

                var studentId = submission.GetProperty("studentId").GetString()!;
                var labId = submission.GetProperty("labId").GetString()!;
                var studentLogin = studentsByLogin.Values
                    .Where(student => student.GetProperty("id").GetString() == studentId)
                    .Select(student => student.GetProperty("login").GetString())
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"Сдача ссылается на неизвестного студента {studentId}.");
                (int Semester, int Number)? pair = null;
                foreach (var kvp in labsByPair)
                {
                    if (kvp.Value.GetProperty("id").GetString() == labId)
                    {
                        pair = kvp.Key;
                        break;
                    }
                }

                if (pair is null)
                {
                    throw new InvalidOperationException($"Сдача ссылается на неизвестную работу {labId}.");
                }

                actualSubmissions.Add((
                    studentLogin,
                    pair.Value.Semester,
                    pair.Value.Number,
                    submission.GetProperty("submitDate").GetString(),
                    submission.GetProperty("defenseDate").ValueKind == JsonValueKind.String
                        ? submission.GetProperty("defenseDate").GetString()
                        : null));
            }

            if (grid.GetProperty("students").GetArrayLength() == 0)
            {
                break;
            }

            Assert.True(++gridPage <= 50, "Обход страниц ведомости не завершается.");
        }

        var expectedSubmissions = new HashSet<(string Login, int Semester, int Number, string? Submit, string? Defense)>
        {
            ("student01", 1, 1, "2026-09-01", "2026-09-11"),
            ("student01", 1, 2, "2026-09-02", "2026-09-12"),
            ("student01", 1, 3, "2026-09-03", null),
            ("student02", 1, 1, "2026-09-01", null),
        };
        Assert.True(
            expectedSubmissions.SetEquals(actualSubmissions),
            $"Сдачи не совпадают с мок-слоем. Ожидались: {Format(expectedSubmissions)}; фактически: {Format(actualSubmissions)}.");

        // --- updatedBy = uuid преподавателя (проверка хранилища тестового хоста). ---
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var teacher = users.GetByLogin("teacher") ?? throw new InvalidOperationException("Сид-преподаватель не найден.");
        var labsRepository = factory.Services.GetRequiredService<ILabRepository>();
        var submissionsRepository = factory.Services.GetRequiredService<ISubmissionRepository>();
        var stored = submissionsRepository.ListByLabIds(labsRepository.GetAll().Select(lab => lab.Id).ToList());
        Assert.Equal(4, stored.Count);
        Assert.All(stored, submission => Assert.Equal((Guid?)teacher.Id, submission.UpdatedBy));
    }

    /// <summary>
    /// Число реальных сдач ведомости (все страницы): запись считается только при
    /// наличии хотя бы одной даты — обе null означают отсутствие записи (FR-025).
    /// </summary>
    private static async Task<int> CountRealSubmissionsAsync(HttpClient client, string groupId, int semester)
    {
        var total = 0;
        var page = 1;
        while (true)
        {
            var grid = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/submissions?semester={semester}&groupId={groupId}&page={page}");
            foreach (var submission in grid.GetProperty("submissions").EnumerateArray())
            {
                if (ApiShapes.IsRealSubmission(submission))
                {
                    ++total;
                }
            }

            if (grid.GetProperty("students").GetArrayLength() == 0)
            {
                return total;
            }

            Assert.True(++page <= 50, "Обход страниц ведомости не завершается.");
        }
    }

    private static string Format(IEnumerable<(string Login, int Semester, int Number, string? Submit, string? Defense)> submissions) =>
        string.Join("; ", submissions
            .OrderBy(item => item.Login, StringComparer.Ordinal)
            .ThenBy(item => item.Number)
            .Select(item => $"{item.Login}:{item.Semester}.{item.Number}({item.Submit ?? "-"}/{item.Defense ?? "-"})"));
}
