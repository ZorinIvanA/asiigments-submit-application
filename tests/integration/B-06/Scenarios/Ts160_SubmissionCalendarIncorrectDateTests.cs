using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-160 «Upsert сдачи: календарно некорректная дата — 400» (boundary, P1, FR-021).
///
/// given: teacher; валидные studentId/labId (свободная пара student05×1.4).
/// when:  PUT /api/v1/submissions {studentId, labId, submitDate:'2026-02-30',
///        defenseDate:null}.
/// then:  400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД']
///        (строгий формат, календарная корректность; FR-021).
/// </summary>
public sealed class Ts160_SubmissionCalendarIncorrectDateTests : IClassFixture<B06SubmissionsWebAppFactory>,
    IAsyncLifetime
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private Guid _studentId;
    private Guid _labId;

    public Ts160_SubmissionCalendarIncorrectDateTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _studentId = (await B06SubmissionsSessions.RequireUserAsync(_factory, "student05")).Id;
        _labId = (await B06SubmissionsSessions.RequireLabAsync(_factory, semester: 1, number: 4)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task PutSubmissionWithFebruary30_Returns400WithDateError()
    {
        // given: teacher-сессия; пара student05×1.4 свободна (валидные studentId/labId).
        Assert.Null(await _factory.Services.GetRequiredService<ISubmissionRepository>()
            .GetByStudentAndLabAsync(_studentId, _labId));
        using var client = (await B06SubmissionsSessions.CreateTeacherSession(_factory)).Client;

        // when: PUT /submissions с несуществующей календарной датой '2026-02-30'.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, _studentId.ToString(), _labId.ToString(),
            submitDate: "2026-02-30", defenseDate: null);

        // then: 400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
