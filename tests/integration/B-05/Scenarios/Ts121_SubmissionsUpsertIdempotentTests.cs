using System.Globalization;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-121 «submissions: повторный и ПАРАЛЛЕЛЬНЫЙ upsert пары (studentId, labId)
/// не создаёт дубля» (concurrency, FR-021/FR-024).
///
/// РЕВ-ISS-001: конкурентный стимул выведенной плитки Ts146 воспроизведён в
/// текущем номере (ранее был заменён на последовательный — инвариант
/// (studentId, labId) из FR-024 оставался без конкурентного оракула);
/// последовательная идемпотентность сохранена в том же кейсе.
///
/// given: пара (student05, работа семестра 1 №1) свободна; сессия teacher
///        (параллельные запросы выполняются одной сессией); шов хранилища сдач
///        доступен (ISubmissionRepository из DI).
/// when:  (1) два последовательных одинаковых PUT /api/v1/submissions
///        {studentId, labId, submitDate:'2026-10-01', defenseDate:null};
///        (2) затем два ПАРАЛЛЕЛЬНЫХ PUT той же пары с идентичным телом
///        {submitDate:'2026-10-02', defenseDate:'2026-10-05'} (барьерный старт).
/// then:  все четыре ответа — 200; в хранилище РОВНО ОДНА запись с этой парой
///        (уникальность (studentId, labId) не обходится ни повтором, ни
///        конкуренцией — FR-024 AC «Атомарность уникальности»); после (1)
///        updatedBy/updatedAt соответствуют последнему запросу; после (2) даты
///        записи равны значениям параллельного тела (тела идентичны — итог
///        детерминирован); исключений и 5xx нет (FR-021 upsert-семантика).
/// </summary>
public sealed class Ts121_SubmissionsUpsertIdempotentTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts121_SubmissionsUpsertIdempotentTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Repeated_And_Parallel_Upserts_StoreExactlyOneRecordWithoutDuplicate()
    {
        // given: пара (student05, работа 1:1) свободна; teacher с известным uuid;
        //        шов хранилища сдач (ISubmissionRepository из DI).
        var student05 = B05SeedLookup.StudentByLogin(_factory, "student05");
        var lab = _factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(1, 1)
            ?? throw new InvalidOperationException(
                "Работа 1:1 не найдена в демо-сиде — given кейса неисполним.");
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        Assert.Null(submissions.GetByStudentAndLab(student05.Id, lab.Id));

        var teacher = B05SeedLookup.Teacher(_factory);
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when (1): два последовательных одинаковых PUT (submitDate '2026-10-01').
        using var first = await client.PutAsync(
            GridEndpoint, UpsertBody(student05.Id, lab.Id, "2026-10-01", defenseDate: null));
        using var second = await client.PutAsync(
            GridEndpoint, UpsertBody(student05.Id, lab.Id, "2026-10-01", defenseDate: null));

        // then (1): оба — 200 (исключений и 5xx нет).
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstRoot = await BodyAssertions.ReadRootObjectAsync(first);
        var secondRoot = await BodyAssertions.ReadRootObjectAsync(second);
        var firstUpdatedAt = ParseTimestamp(firstRoot, "первый PUT");
        var secondUpdatedAt = ParseTimestamp(secondRoot, "второй PUT");
        Assert.True(
            secondUpdatedAt >= firstUpdatedAt,
            "Метка второго запроса раньше метки первого — нарушен порядок upsert.");

        // then (1): в хранилище РОВНО ОДНА запись пары; updatedBy/updatedAt —
        //           от последнего (второго) запроса.
        var storedAfterSequential = submissions.GetByPairs([student05.Id], [lab.Id]);
        Assert.Single(storedAfterSequential);
        var sequentialRecord = storedAfterSequential[0];
        Assert.Equal(teacher.Id, sequentialRecord.UpdatedBy);
        Assert.Equal(secondUpdatedAt, sequentialRecord.UpdatedAt);

        // when (2): два ПАРАЛЛЕЛЬНЫХ PUT той же пары с идентичным телом
        //           {submitDate:'2026-10-02', defenseDate:'2026-10-05'};
        //           барьерный старт — оба запроса выпускаются одновременно
        //           (одна сессия teacher).
        var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parallelFirst = Task.Run(async () =>
        {
            await startBarrier.Task;
            return await client.PutAsync(
                GridEndpoint, UpsertBody(student05.Id, lab.Id, "2026-10-02", defenseDate: "2026-10-05"));
        });
        var parallelSecond = Task.Run(async () =>
        {
            await startBarrier.Task;
            return await client.PutAsync(
                GridEndpoint, UpsertBody(student05.Id, lab.Id, "2026-10-02", defenseDate: "2026-10-05"));
        });
        startBarrier.SetResult();
        var parallelResponses = await Task.WhenAll(parallelFirst, parallelSecond);

        // then (2): оба параллельных ответа — 200 (исключений и 5xx нет);
        //           даты записи в ответах равны значениям параллельного тела.
        using var third = parallelResponses[0];
        using var fourth = parallelResponses[1];
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fourth.StatusCode);

        var thirdRoot = await BodyAssertions.ReadRootObjectAsync(third);
        var fourthRoot = await BodyAssertions.ReadRootObjectAsync(fourth);
        foreach (var root in new[] { thirdRoot, fourthRoot })
        {
            Assert.Equal("2026-10-02", root.GetProperty("submitDate").GetString());
            Assert.Equal("2026-10-05", root.GetProperty("defenseDate").GetString());
        }

        // then (2): в хранилище по-прежнему РОВНО ОДНА запись пары (уникальность
        //           (studentId, labId) не обходится конкуренцией — FR-024); её
        //           даты равны значениям параллельного тела (тела идентичны —
        //           итог детерминирован), updatedBy — преподаватель сессии.
        var storedAfterParallel = submissions.GetByPairs([student05.Id], [lab.Id]);
        Assert.Single(storedAfterParallel);
        var parallelRecord = storedAfterParallel[0];
        Assert.Equal(new DateOnly(2026, 10, 2), parallelRecord.SubmitDate);
        Assert.Equal(new DateOnly(2026, 10, 5), parallelRecord.DefenseDate);
        Assert.Equal(teacher.Id, parallelRecord.UpdatedBy);
    }

    /// <summary>Тело PUT /submissions с датами в контрактном формате 'ГГГГ-ММ-ДД'.</summary>
    private static StringContent UpsertBody(Guid studentId, Guid labId, string submitDate, string? defenseDate) =>
        new(
            "{\"studentId\":\"" + studentId + "\",\"labId\":\"" + labId + "\"," +
            "\"submitDate\":\"" + submitDate + "\"," +
            "\"defenseDate\":" + (defenseDate is null ? "null" : $"\"{defenseDate}\"") + "}",
            Encoding.UTF8,
            "application/json");

    /// <summary>Разбирает updatedAt ответа как ISO-8601 (UTC) — иначе падение с контекстом.</summary>
    private static DateTime ParseTimestamp(JsonElement root, string label)
    {
        var raw = root.GetProperty("updatedAt").GetString();
        Assert.True(
            DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed),
            $"updatedAt {label} «{raw}» не разбирается как ISO-8601.");
        return parsed;
    }
}
