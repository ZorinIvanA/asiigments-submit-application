using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-117 «submissions: несуществующая группа — 404» (negative, FR-021).
///
/// given: группы с указанным uuid нет (случайный Guid); сессия teacher.
/// when:  GET /api/v1/submissions?groupId=&lt;неизвестный&gt;&amp;semester=1&amp;page=1
/// then:  404 'Группа не найдена' (FR-021 AC «Нет группы — 404»).
/// </summary>
public sealed class Ts117_SubmissionsUnknownGroup404Tests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";

    private readonly B05WebAppFactory _factory;

    public Ts117_SubmissionsUnknownGroup404Tests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownGroupId_Returns404GroupNotFound()
    {
        // given: группа с таким uuid в хранилище отсутствует (проверка по шву).
        var unknownGroupId = Guid.NewGuid();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        Assert.Null(groups.GetById(unknownGroupId));

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: ведомость по неизвестной группе.
        using var response = await client.GetAsync(
            $"{GridEndpoint}?groupId={unknownGroupId}&semester=1&page=1");

        // then: 404 'Группа не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }
}
