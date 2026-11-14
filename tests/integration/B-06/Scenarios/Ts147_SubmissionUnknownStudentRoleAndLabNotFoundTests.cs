using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-147 «Сдачи: неизвестный студент/преподаватель и неизвестная работа — 404»
/// (negative, P1, FR-021).
///
/// given: teacher; даты валидного формата.
/// when:  PUT /submissions с studentId=&lt;uuid преподавателя&gt;; затем с
///        labId=&lt;несуществующий uuid&gt; (studentId валиден).
/// then:  первый — 404 'Студент не найден' (роль ≠ student); второй —
///        404 'Лабораторная не найдена'.
/// </summary>
public sealed class Ts147_SubmissionUnknownStudentRoleAndLabNotFoundTests :
    IClassFixture<B06SubmissionsWebAppFactory>,
    IAsyncLifetime
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private Guid _existingStudentId;

    public Ts147_SubmissionUnknownStudentRoleAndLabNotFoundTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _existingStudentId = (await B06SubmissionsSessions.RequireUserAsync(_factory, "student05")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task PutSubmissionWithTeacherUuidAsStudent_Returns404StudentNotFound()
    {
        // given: teacher-сессия; uuid преподавателя известен тесту; даты валидны (null).
        var (client, teacherId) = await B06SubmissionsSessions.CreateTeacherSession(_factory);
        using var _client = client;
        Assert.Equal(UserRoles.Teacher, (await B06SubmissionsSessions.RequireUserAsync(_factory, "teacher")).Role);

        // when: PUT /submissions {studentId:<uuid преподавателя>, labId:<валидный>, null, null}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            _client,
            studentId: teacherId.ToString(),
            labId: (await B06SubmissionsSessions.RequireLabAsync(_factory, semester: 1, number: 1)).Id.ToString(),
            submitDate: null,
            defenseDate: null);

        // then: 404 'Студент не найден' — пользователь существует, но роль ≠ student.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task PutSubmissionWithUnknownLab_Returns404LabNotFound()
    {
        // given: teacher-сессия; studentId валиден; labId не существует; даты валидны (null).
        using var client = (await B06SubmissionsSessions.CreateTeacherSession(_factory)).Client;

        // when: PUT /submissions {валидный studentId, несуществующий labId, null, null}.
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
