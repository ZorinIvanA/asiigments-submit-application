using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-150 «Upsert сдачи: студент и работа не найдены — 404» (негативный, P0, FR-021).
///
/// given: сессия teacher; даты валидны (null).
/// when:  PUT /api/v1/submissions с несуществующим studentId (работа существует);
///        затем PUT с существующим studentId и несуществующим labId.
/// then:  первый — 404 'Студент не найден'; второй — 404 'Лабораторная не найдена'
///        (FR-021: проверки сущностей).
/// </summary>
public sealed class Ts150_SubmissionUnknownEntitiesTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _existingStudentId;
    private readonly Guid _existingLabId;

    public Ts150_SubmissionUnknownEntitiesTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _existingStudentId = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
        _existingLabId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
    }

    [Fact]
    public async Task PutSubmissionWithUnknownStudent_Returns404StudentNotFound()
    {
        // given: сессия teacher; даты валидны (null); studentId не существует.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

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
        // given: сессия teacher; даты валидны (null); labId не существует.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

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
