using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-135 «Репозитории: атомарность уникальности (semester, number) при
/// параллельном создании» (concurrency, P0, FR-024 AC «Атомарность уникальности»,
/// FR-017). 
///
/// given: сессия teacher; пара (semester=7, number=1) свободна.
/// when:  два параллельных POST /labs с одной парой (semester=7, number=1).
/// then:  ровно один ответ 201, второй — 409 «Лабораторная с таким номером уже
///        есть в семестре»; без исключений и дублей пары (в хранилище ровно одна
///        работа (7,1), её id совпадает с id из тела 201).
/// </summary>
public sealed class Ts135_LabPairUniquenessParallelTests : IClassFixture<B06WebAppFactory>
{
    private const int Semester = 7;
    private const int Number = 1;

    private readonly B06WebAppFactory _factory;

    public Ts135_LabPairUniquenessParallelTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwoParallelCreatesWithSamePair_ExactlyOneCreatedOtherConflict()
    {
        // given: сессия teacher (сид-преподаватель фикстуры, ADR-015); пара (7,1) свободна.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        Assert.Empty(labs.GetAll());
        Assert.Null(labs.TryGetByPair(Semester, Number));

        using var firstClient = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        using var secondClient = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: два параллельных POST /labs с одной парой (стартовый барьер —
        // реальное столкновение, как в стресс-гейтах хранилища).
        using var startGate = new ManualResetEventSlim();
        var firstTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await PostLabAsync(firstClient, "Парная работа (7:1), попытка А");
        });
        var secondTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await PostLabAsync(secondClient, "Парная работа (7:1), попытка Б");
        });

        startGate.Set();
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        // then: ровно один ответ 201, второй — 409 (никаких 5xx — «без исключений»).
        var responses = new[] { firstResponse, secondResponse };
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        var createdResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var created = await BodyAssertions.ReadRootObjectAsync(createdResponse);
        var createdId = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(createdId), "Ожидался непустой id созданной работы в теле 201.");

        using var conflictResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflict = await BodyAssertions.ReadRootObjectAsync(conflictResponse);
        BodyAssertions.MessageIs(conflict, ErrorTexts.DuplicateLab);

        // then: дублей пары нет — в хранилище ровно одна работа (7,1), id совпадает с 201.
        var stored = labs.TryGetByPair(Semester, Number);
        Assert.NotNull(stored);
        Assert.Single(labs.GetAll());
        Assert.Equal(createdId, stored!.Id.ToString());
        Assert.Equal(Number, stored.Number);
        Assert.Equal(Semester, stored.Semester);
    }

    private static Task<HttpResponseMessage> PostLabAsync(HttpClient client, string content) =>
        client.PostAsJsonAsync(ApiRequests.LabsEndpoint, new
        {
            number = Number,
            semester = Semester,
            content,
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });
}
