using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-146 «Сдачи: календарная корректность даты» (boundary, P1, FR-021).
///
/// given: teacher; валидные studentId/labId; пара свободна.
/// when:  PUT с submitDate:'2026-02-30' (несуществующая дата, пара student05×1.1);
///        затем PUT (другая свободная пара student06×1.1) с '2024-02-29'
///        (високосная).
/// then:  первая — 400 errors.submitDate=['Дата должна быть строкой в формате
///        ГГГГ-ММ-ДД'] (строгий формат, календарно корректная дата); вторая —
///        200 (FR-021: «не null и не строка 'YYYY-MM-DD' (строгий формат,
///        календарная корректность) → 400»).
/// </summary>
public sealed class Ts146_SubmissionCalendarDateCorrectnessTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly Guid _student05Id;
    private readonly Guid _student06Id;
    private readonly Guid _labId;

    public Ts146_SubmissionCalendarDateCorrectnessTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _student05Id = B06SubmissionsSessions.RequireUser(factory, "student05").Id;
        _student06Id = B06SubmissionsSessions.RequireUser(factory, "student06").Id;
        _labId = B06SubmissionsSessions.RequireLab(factory, semester: 1, number: 1).Id;
    }

    [Fact]
    public async Task PutSubmissionWithFebruary30_IsRejectedAndLeapYearDateIsAccepted()
    {
        // given: teacher-сессия; обе пары (student05×1.1, student06×1.1) свободны.
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        Assert.Null(submissions.GetByStudentAndLab(_student05Id, _labId));
        Assert.Null(submissions.GetByStudentAndLab(_student06Id, _labId));
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: PUT на первую свободную пару с несуществующей датой '2026-02-30'.
        using var impossible = await B06SubmissionsApi.PutSubmissionAsync(
            client, _student05Id.ToString(), _labId.ToString(), "2026-02-30", defenseDate: null);

        // then: 400 errors.submitDate словарным текстом (строгий формат + календарь).
        Assert.Equal(HttpStatusCode.BadRequest, impossible.StatusCode);
        var impossibleRoot = await BodyAssertions.ReadRootObjectAsync(impossible);
        BodyAssertions.ErrorFieldIsExactly(
            impossibleRoot, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");

        // when: PUT на другую свободную пару с високосной датой '2024-02-29'.
        using var leapDay = await B06SubmissionsApi.PutSubmissionAsync(
            client, _student06Id.ToString(), _labId.ToString(), "2024-02-29", defenseDate: null);

        // then: 200 — календарно корректная дата контрактна.
        Assert.Equal(HttpStatusCode.OK, leapDay.StatusCode);
        var leapDayRoot = await BodyAssertions.ReadRootObjectAsync(leapDay);
        Assert.Equal("2024-02-29", leapDayRoot.GetProperty("submitDate").GetString());
    }
}
