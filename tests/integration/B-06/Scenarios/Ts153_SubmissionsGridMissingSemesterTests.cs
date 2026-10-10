using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-153 «Ведомость: отсутствует semester — 400» (негативный, P0, FR-021).
///
/// given: groupId указывает на существующую группу (ИК-221 демо-набора);
///        сессия teacher.
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;page=1 (semester
///        не передаётся вовсе).
/// then:  400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10'] (строгий контракт;
///        FR-021 AC «Отсутствует semester»).
/// </summary>
public sealed class Ts153_SubmissionsGridMissingSemesterTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts153_SubmissionsGridMissingSemesterTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGridWithoutSemester_Returns400WithStrictSemesterError()
    {
        // given: groupId — существующая группа; сессия teacher.
        var groupId = B06SubmissionsSessions.RequireGroup(_factory, "ИК-221").Id.ToString();
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: GET /submissions?groupId=<валидный>&page=1 — без semester.
        using var response = await B06SubmissionsApi.GetGridAsync(
            client, groupId, semester: null, page: "1");

        // then: 400 'Данные заполнены неверно' + errors.semester=['Семестр — число от 1 до 10'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "semester", "Семестр — число от 1 до 10");
    }
}
