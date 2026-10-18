using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-155 «Ведомость: отсутствует groupId — 400» (негативный, P0, FR-021).
///
/// given: сессия teacher; semester валиден (в демо-наборе есть работы семестра 1).
/// when:  GET /api/v1/submissions?semester=1&amp;page=1 (без groupId — параметр
///        не передаётся вовсе).
/// then:  400 'Данные заполнены неверно' + errors.groupId=['Заполните поле']
///        (FR-021: «groupId отсутствует → 400»).
/// </summary>
public sealed class Ts155_SubmissionsGridMissingGroupIdTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts155_SubmissionsGridMissingGroupIdTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetGridWithoutGroupId_Returns400WithRequiredGroupIdError()
    {
        // given: сессия teacher.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: GET /submissions?semester=1&page=1 — без groupId.
        using var response = await B06SubmissionsApi.GetGridAsync(
            client, groupId: null, semester: "1", page: "1");

        // then: 400 'Данные заполнены неверно' + errors.groupId=['Заполните поле'].
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "groupId", "Заполните поле");
    }
}
