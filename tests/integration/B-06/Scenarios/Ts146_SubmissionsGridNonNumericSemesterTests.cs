using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-146 «Ведомость: нечисловой semester — 400» (негативный, P1, FR-021).
///
/// given: сессия teacher; groupId валиден (демо-группа ИК-221).
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=abc
/// then:  400 'Данные заполнены неверно' с
///        errors.semester=['Семестр — число от 1 до 10']
///        (строгий контракт: нечисловое значение недопустимо, в отличие от /labs —
///        ISS-011/AR-002/ADR-011; Labs__MaxSemester=10 по умолчанию).
/// </summary>
public sealed class Ts146_SubmissionsGridNonNumericSemesterTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts146_SubmissionsGridNonNumericSemesterTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGridWithNonNumericSemester_Returns400WithSemesterError()
    {
        // given: сессия teacher; валидный groupId демо-группы ИК-221.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        var groupId = B06SubmissionsSessions.RequireGroup(_factory, "ИК-221").Id.ToString();

        // when: GET /submissions?groupId=<валидный>&semester=abc.
        using var response = await B06SubmissionsApi.GetGridAsync(
            client, groupId, semester: "abc", page: "1");

        // then: 400 с errors.semester=['Семестр — число от 1 до 10'] (не 200 с пустой выборкой).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "semester", "Семестр — число от 1 до 10");
    }
}
