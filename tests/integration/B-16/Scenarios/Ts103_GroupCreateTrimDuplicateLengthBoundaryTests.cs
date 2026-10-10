using System.Text.Json;
using LabsApp.IntegrationTests.B16.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-103 «groups: создание с тримом, дубликат (последовательный и
/// ПАРАЛЛЕЛЬНЫЙ), границы длины имени» (boundary, FR-019, FR-024, P0;
/// РЕВ-ISS-001).
///
/// given: сессия teacher; группы ИК-224 и ИК-230 не существуют (демо-сид содержит
///        только ИК-221/222/223).
/// when:  POST /groups {name:'  ИК-224  '}; затем POST {name:'ик-224'}; затем два
///        ПАРАЛЛЕЛЬНЫХ POST /groups {name:'ИК-230'} (барьерный старт, один и тот
///        же lower(name)); отдельно POST с name длиной ровно 100 и длиной 101.
/// then:  первый — 201 {name:'ИК-224', studentCount:0}; второй — 409 «Группа с
///        таким названием уже существует»; параллельная пара — ровно один ответ
///        201 GroupDto, второй — 409 «Группа с таким названием уже существует»,
///        в хранилище ровно одна группа с lower(name)='ик-230' (дублей и
///        исключений нет); name длиной 100 — 201; длиной 101 — 400 с
///        errors.name=['Название группы — от 1 до 100 символов']
///        (FR-019 AC «Создание и дубликат»; FR-024 AC «Атомарность
///        уникальности» — lower(group.name)).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts103_GroupCreateTrimDuplicateLengthBoundaryTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string ConflictText = "Группа с таким названием уже существует";
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts103_GroupCreateTrimDuplicateLengthBoundaryTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_TrimsName_ThenCiDuplicateConflicts()
    {
        // given: сессия teacher; группы ИК-224 нет.
        using var client = B16GroupsSession.CreateTeacher(_factory);

        // when: POST /groups {name:'  ИК-224  '}.
        using var created = await B16GroupsApi.PostGroupAsync(client, "  ИК-224  ");

        // then: 201 {name:'ИК-224', studentCount:0} — имя триммлено, счётчик пустой группы.
        using var createdBody = await B16GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:'  ИК-224  '} (teacher)");
        Assert.Equal("ИК-224", B16GroupsApi.ReadString(createdBody.RootElement, "name"));
        Assert.Equal(0, B16GroupsApi.ReadInt(createdBody.RootElement, "studentCount"));

        // when: POST {name:'ик-224'} — дубликат lower(name).
        using var duplicate = await B16GroupsApi.PostGroupAsync(client, "ик-224");

        // then: 409 'Группа с таким названием уже существует'.
        using var duplicateBody = await B16GroupsApi.ParseWithStatusAsync(
            duplicate, HttpStatusCode.Conflict, "POST /groups {name:'ик-224'} (teacher)");
        B16GroupsApi.MessageIs(duplicateBody.RootElement, ConflictText);
    }

    [Fact]
    public async Task PostGroup_ParallelSameName_ExactlyOneCreatedOneConflict()
    {
        // given: сессия teacher; группы ИК-230 не существует; инвариант
        // lower(group.name) из FR-024 оспаривается конкуренцией (РЕВ-ISS-001).
        using var client = B16GroupsSession.CreateTeacher(_factory);

        // when: два ПАРАЛЛЕЛЬНЫХ POST /groups {name:'ИК-230'} — барьерный старт:
        // обе задачи готовятся, отправляются одновременно после барьера.
        var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await startBarrier.Task;
            using var response = await B16GroupsApi.PostGroupAsync(client, "ИК-230");
            return (status: (int)response.StatusCode, body: await response.Content.ReadAsStringAsync());
        });
        var secondTask = Task.Run(async () =>
        {
            await startBarrier.Task;
            using var response = await B16GroupsApi.PostGroupAsync(client, "ИК-230");
            return (status: (int)response.StatusCode, body: await response.Content.ReadAsStringAsync());
        });
        startBarrier.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        // then: ровно один ответ 201 GroupDto, второй — 409 с точным текстом;
        // ни один запрос не завершился исключением/иной ошибкой (500/429 и пр.).
        var first = await firstTask;
        var second = await secondTask;
        var responses = new[] { first, second };
        Assert.True(
            responses.Count(response => response.status == (int)HttpStatusCode.Created) == 1,
            "Ожидался ровно один ответ 201 параллельной пары POST /groups {name:'ИК-230'}, фактически: " +
            $"[{responses[0].status}: {responses[0].body}] | [{responses[1].status}: {responses[1].body}]");
        Assert.True(
            responses.Count(response => response.status == (int)HttpStatusCode.Conflict) == 1,
            "Ожидался ровно один ответ 409 параллельной пары POST /groups {name:'ИК-230'}, фактически: " +
            $"[{responses[0].status}: {responses[0].body}] | [{responses[1].status}: {responses[1].body}]");
        foreach (var response in responses.Where(response => response.status == (int)HttpStatusCode.Conflict))
        {
            using var conflictBody = JsonDocument.Parse(response.body);
            B16GroupsApi.MessageIs(conflictBody.RootElement, ConflictText);
        }

        // then: в хранилище ровно одна группа с lower(name)='ик-230' (FR-024:
        // уникальность проверяется атомарно относительно конкурентных созданий;
        // дублей нет).
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var ciMatches = B16GroupsApi.CountStoredGroupsByCiName(groups, "ик-230");
        Assert.True(
            ciMatches == 1,
            $"Ожидалась ровно одна группа с lower(name)='ик-230' в хранилище, фактически {ciMatches}: " +
            $"[{string.Join(", ", B16GroupsApi.StoredGroupNames(groups))}].");
    }

    [Fact]
    public async Task PostGroup_NameOf100CharsCreated_NameOf101Rejected()
    {
        // given: сессия teacher; имена граничной длины без пробельных символов
        // (после трима длина сохраняется); в хранилище таких имён нет.
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var name100 = new string('Г', 100);
        var name101 = new string('Г', 101);
        Assert.Equal(100, name100.Length);
        Assert.Equal(101, name101.Length);

        // when: POST с name длиной ровно 100.
        using var created = await B16GroupsApi.PostGroupAsync(client, name100);

        // then: 201, имя без изменений.
        using var createdBody = await B16GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:100 символов} (teacher)");
        Assert.Equal(name100, B16GroupsApi.ReadString(createdBody.RootElement, "name"));

        // when: POST с name длиной 101.
        using var rejected = await B16GroupsApi.PostGroupAsync(client, name101);

        // then: 400 с errors.name=['Название группы — от 1 до 100 символов'].
        using var rejectedBody = await B16GroupsApi.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /groups {name:101 символ} (teacher)");
        B16GroupsApi.FieldErrorsExactly(rejectedBody.RootElement, "name", NameValidationText);
    }
}
