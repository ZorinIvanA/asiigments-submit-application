using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-150 «Upsert сдачи: студент и работа не найдены — 404» (негативный, P0, FR-021).
///
/// given: анонимный клиент; даты валидны (null).
/// when:  PUT /api/v1/submissions с несуществующим studentId (работа существует);
///        затем PUT с существующим studentId и несуществующим labId.
/// then:  первый — 404 'Студент не найден'; второй — 404 'Лабораторная не найдена'
///        (FR-021: проверки сущностей).
/// </summary>
public sealed class Ts150_SubmissionUnknownEntitiesTests : IClassFixture<B06SubmissionsWebAppFactory>,
    IAsyncLifetime
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private Guid _existingStudentId;
    private Guid _existingLabId;

    public Ts150_SubmissionUnknownEntitiesTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _existingStudentId = (await B06SubmissionsSessions.RequireUserAsync(_factory, "student05")).Id;
        _existingLabId = (await B06SubmissionsSessions.RequireLabAsync(_factory, semester: 1, number: 1)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task PutSubmissionWithUnknownStudent_Returns404StudentNotFound()
    {
        // given: анонимный клиент; даты валидны (null); studentId не существует.
        using var client = (await B06SubmissionsSessions.CreateTeacherClientAsync(_factory)).Client;

        // when: PUT /submissions {неизвестный studentId, существующий labId, null, null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client,
            studentId: Guid.NewGuid().ToString(),
            labId: _existingLabId.ToString(),
            submitDate: null,
            defenseDate: null);

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task PutSubmissionWithUnknownLab_Returns404LabNotFound()
    {
        // given: анонимный клиент; даты валидны (null); labId не существует.
        using var client = (await B06SubmissionsSessions.CreateTeacherClientAsync(_factory)).Client;

        // when: PUT /submissions {существующий studentId, неизвестный labId, null, null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client,
            studentId: _existingStudentId.ToString(),
            labId: Guid.NewGuid().ToString(),
            submitDate: null,
            defenseDate: null);

        // then: 404 'Лабораторная не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }
}
