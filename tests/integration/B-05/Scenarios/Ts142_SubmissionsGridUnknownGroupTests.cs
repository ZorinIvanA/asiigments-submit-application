using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-142 «Ведомость: группа не найдена — 404» (negative, FR-021).
///
/// given: группы с указанным groupId не существует; semester=1 валиден.
/// when:  GET /api/v1/submissions?groupId=&lt;неизвестный&gt;&amp;semester=1&amp;page=1
/// then:  404; message 'Группа не найдена' (FR-021 AC «Нет группы — 404»).
/// </summary>
public sealed class Ts142_SubmissionsGridUnknownGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts142_SubmissionsGridUnknownGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownGroupId_ReturnsGroupNotFound()
    {
        // given: неизвестный uuid группы; teacher авторизован.
        var unknownGroupId = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: ведомость по несуществующей группе.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={unknownGroupId}&semester=1&page=1");

        // then: 404 'Группа не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }
}
