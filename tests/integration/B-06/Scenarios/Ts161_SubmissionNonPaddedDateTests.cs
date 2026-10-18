using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-161 «Upsert сдачи: непаддированная дата — 400» (boundary, P2, FR-021).
///
/// given: teacher; валидные studentId/labId (свободная пара student05×1.4).
/// when:  PUT /api/v1/submissions {studentId, labId, submitDate:'2026-9-5',
///        defenseDate:null}.
/// then:  400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД']
///        (формат строго 'YYYY-MM-DD').
/// </summary>
public sealed class Ts161_SubmissionNonPaddedDateTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _studentId;
    private readonly Guid _labId;

    public Ts161_SubmissionNonPaddedDateTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _studentId = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 4).Id;
    }

    [Fact]
    public async Task PutSubmissionWithNonPaddedDate_Returns400WithDateError()
    {
        // given: teacher-сессия; пара student05×1.4 свободна (валидные studentId/labId).
        Assert.Null(_factory.Services.GetRequiredService<ISubmissionRepository>()
            .GetByStudentAndLab(_studentId, _labId));
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: PUT /submissions с непаддированной датой '2026-9-5'.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, _studentId.ToString(), _labId.ToString(),
            submitDate: "2026-9-5", defenseDate: null);

        // then: 400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
