using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-201 «Лабораторные: DELETE несуществующего id — 404» (негативный, P1, FR-017).
///
/// given: записи с указанным uuid не существует (случайный uuid); сессия teacher.
/// when:  DELETE /api/v1/labs/&lt;произвольный-uuid&gt;.
/// then:  404; message 'Лабораторная не найдена'
///        (NOT_FOUND_LAB; FR-017: «DELETE /labs/{id} → 204 и каскадное удаление …
///        (несуществующий id → 404)»).
/// </summary>
public sealed class Ts201_DeleteUnknownLabTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts201_DeleteUnknownLabTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteLabByUnknownId_Returns404LabNotFound()
    {
        // given: сессия teacher; uuid, которого нет в хранилище.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: DELETE /labs/<произвольный-uuid>.
        using var response = await B06SubmissionsApi.DeleteLabByIdAsync(client, Guid.NewGuid().ToString());

        // then: 404 'Лабораторная не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }
}
