using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-152 (P0, negative; FR-021 AC «Нет группы — 404») «Ведомость: неизвестная
/// группа — 404».
/// given: группы с указанным groupId не существует (случайный uuid — проверка по
///        шву хранилища); анонимный доступ (текущий пользователь — сид-преподаватель).
/// when:  GET /submissions?groupId=&lt;случайный-uuid&gt;&amp;semester=1&amp;page=1
/// then:  404 'Группа не найдена'.
/// </summary>
public sealed class Ts152_SubmissionsGridUnknownGroupId404Tests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts152_SubmissionsGridUnknownGroupId404Tests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownGroupId_Returns404GroupNotFound()
    {
        // given: группа с таким uuid в хранилище отсутствует; анонимный запрос (текущий пользователь — сид-преподаватель).
        var unknownGroupId = Guid.NewGuid();
        Assert.Null(await _factory.Services.GetRequiredService<IGroupRepository>().GetByIdAsync(unknownGroupId));
        using var client = HostClients.Create(_factory);

        // when: ведомость по неизвестной группе.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={unknownGroupId}&semester=1&page=1");

        // then: 404 'Группа не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }
}
