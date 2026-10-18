using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-162 «Репозитории: параллельное создание одной пары (semester,number) —
/// один 201» (concurrency, P0, FR-024 AC «Атомарность уникальности»).
///
/// given: пара (semester=7, number=99) свободна; teacher; два параллельных
///        клиента (барьер старта).
/// when:  два одновременных POST /labs с одной парой (7,99) и валидными
///        прочими полями.
/// then:  ровно один ответ 201, второй — 409 CONFLICT_LAB; без исключений;
///        в хранилище одна запись пары.
/// </summary>
public sealed class Ts162_LabPair799ParallelUniquenessTests : IClassFixture<B07WebAppFactory>
{
    private const int Semester = 7;
    private const int Number = 99;

    private readonly B07WebAppFactory _factory;

    public Ts162_LabPair799ParallelUniquenessTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwoConcurrentCreatesOfPair7And99_ExactlyOne201Other409_SingleStoredRecord()
    {
        // given: пара (7,99) свободна; teacher (сид-преподаватель фикстуры,
        // ADR-015); два параллельных клиента со стартовым барьером.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        Assert.Empty(labs.GetAll());
        Assert.Null(labs.TryGetByPair(Semester, Number));

        using var firstClient = B07MintedSessions.CreateTeacherClient(_factory);
        using var secondClient = B07MintedSessions.CreateTeacherClient(_factory);

        // when: два одновременных POST /labs с одной парой (7,99).
        using var startGate = new ManualResetEventSlim();
        var firstTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await PostLabAsync(firstClient, "Работа (7:99), попытка А");
        });
        var secondTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await PostLabAsync(secondClient, "Работа (7:99), попытка Б");
        });

        startGate.Set();
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        // then: ровно один ответ 201, второй — 409; ни одного 5xx («без исключений»).
        var responses = new[] { firstResponse, secondResponse };
        Assert.All(responses, response => Assert.True((int)response.StatusCode < 500, $"Неожиданный 5xx: {(int)response.StatusCode}"));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        // then: 409 CONFLICT_LAB — конверт ошибки с дословным текстом
        // «Лабораторная с таким номером уже есть в семестре».
        using var conflictResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflict = await BodyAssertions.ReadRootObjectAsync(conflictResponse);
        BodyAssertions.MessageIs(conflict, ErrorTexts.DuplicateLab);

        // then: в хранилище одна запись пары (7,99); id совпадает с телом 201.
        var createdResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var created = await BodyAssertions.ReadRootObjectAsync(createdResponse);
        var createdId = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(createdId), "Ожидался непустой id созданной работы в теле 201.");

        var stored = labs.TryGetByPair(Semester, Number);
        Assert.NotNull(stored);
        Assert.Single(labs.GetAll());
        Assert.Equal(createdId, stored!.Id.ToString());
        Assert.Equal(Number, stored.Number);
        Assert.Equal(Semester, stored.Semester);
    }

    private static Task<HttpResponseMessage> PostLabAsync(HttpClient client, string content) =>
        client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = Number,
            semester = Semester,
            content,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });
}
