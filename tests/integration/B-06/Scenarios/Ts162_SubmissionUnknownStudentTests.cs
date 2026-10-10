using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-162 «Upsert сдачи: неизвестный студент — 404» (негативный, P0, FR-021).
///
/// given: teacher; labId валиден (работа 1.1); studentId — случайный uuid;
///        даты валидны.
/// when:  PUT /api/v1/submissions {studentId:&lt;случайный uuid&gt;, labId:&lt;1.1&gt;,
///        submitDate:'2026-09-20', defenseDate:null}.
/// then:  404 'Студент не найден' (FR-021).
/// </summary>
public sealed class Ts162_SubmissionUnknownStudentTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _labId;

    public Ts162_SubmissionUnknownStudentTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
    }

    [Fact]
    public async Task PutSubmissionWithUnknownStudent_Returns404StudentNotFound()
    {
        // given: teacher-сессия; studentId — случайный uuid; labId валиден; даты валидны.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        var unknownStudentId = Guid.NewGuid();

        // when: PUT /submissions {studentId:<случайный uuid>, labId:<валидный>, ...}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, unknownStudentId.ToString(), _labId.ToString(),
            submitDate: "2026-09-20", defenseDate: null);

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }
}
