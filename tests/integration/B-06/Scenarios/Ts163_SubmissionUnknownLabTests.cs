using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-163 «Upsert сдачи: неизвестная работа — 404» (негативный, P0, FR-021).
///
/// given: teacher; studentId валиден (student05); labId — случайный uuid;
///        даты валидны.
/// when:  PUT /api/v1/submissions {studentId:&lt;student05&gt;, labId:&lt;случайный
///        uuid&gt;, submitDate:'2026-09-20', defenseDate:null}.
/// then:  404 'Лабораторная не найдена' (FR-021).
/// </summary>
public sealed class Ts163_SubmissionUnknownLabTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;

    public Ts163_SubmissionUnknownLabTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
    }

    [Fact]
    public async Task PutSubmissionWithUnknownLab_Returns404LabNotFound()
    {
        // given: teacher-сессия; studentId валиден; labId — случайный uuid; даты валидны.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        var unknownLabId = Guid.NewGuid();

        // when: PUT /submissions {studentId:<валидный>, labId:<случайный uuid>, ...}.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, _studentId.ToString(), unknownLabId.ToString(),
            submitDate: "2026-09-20", defenseDate: null);

        // then: 404 'Лабораторная не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }
}
