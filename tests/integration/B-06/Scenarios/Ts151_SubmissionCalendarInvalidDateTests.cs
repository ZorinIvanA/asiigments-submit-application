using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-151 «Upsert сдачи: календарно некорректная дата — 400» (boundary, P1, FR-021).
///
/// given: анонимный клиент; сущности существуют (student05, работа 1.1 демо-набора).
/// when:  PUT /api/v1/submissions {…, submitDate:'2026-02-30', defenseDate:null}
///        — 30 февраля не существует.
/// then:  400; errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД']
///        (строгий формат, календарная корректность; ASM-014).
/// </summary>
public sealed class Ts151_SubmissionCalendarInvalidDateTests : IClassFixture<B06SubmissionsWebAppFactory>,
    IAsyncLifetime
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private Guid _studentId;
    private Guid _labId;

    public Ts151_SubmissionCalendarInvalidDateTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _studentId = (await B06SubmissionsSessions.RequireUserAsync(_factory, "student05")).Id;
        _labId = (await B06SubmissionsSessions.RequireLabAsync(_factory, semester: 1, number: 1)).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task PutSubmissionWithCalendarImpossibleDate_Returns400WithDateError()
    {
        // given: анонимный клиент; сущности существуют.
        using var client = (await B06SubmissionsSessions.CreateTeacherClientAsync(_factory)).Client;

        // when: PUT /submissions с датой '2026-02-30'.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, _studentId.ToString(), _labId.ToString(), "2026-02-30", defenseDate: null);

        // then: 400 с errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
