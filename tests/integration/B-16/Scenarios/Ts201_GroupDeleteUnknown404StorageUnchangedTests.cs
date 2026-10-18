using LabsApp.IntegrationTests.B16.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-201 «Группы: DELETE несуществующей группы — 404» (negative, FR-019, P1).
///
/// given: случайный uuid, не принадлежащий никакой группе; сессия teacher.
/// when:  DELETE /api/v1/groups/&lt;uuid&gt;.
/// then:  404 'Группа не найдена'; хранилище групп не изменилось (FR-019: «404
///        при неизвестном id»; интерфейс DELETE /groups/{id} — код
///        NOT_FOUND_GROUP).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts201_GroupDeleteUnknown404StorageUnchangedTests : IClassFixture<B16GroupsDemoDataFactory>
{
    private const string NotFoundText = "Группа не найдена";

    private readonly B16GroupsDemoDataFactory _factory;

    public Ts201_GroupDeleteUnknown404StorageUnchangedTests(B16GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteUnknownGroup_Returns404_StorageUnchanged()
    {
        // given: сессия teacher; случайный uuid; снимок перечня групп хранилища
        // (демо-сид: ИК-221, ИК-222, ИК-223).
        using var client = B16GroupsSession.CreateTeacher(_factory);
        var unknownId = Guid.NewGuid();
        var groupsRepo = _factory.Services.GetRequiredService<IGroupRepository>();
        var namesBefore = B16GroupsApi.StoredGroupNames(groupsRepo);
        Assert.True(
            namesBefore.Count == 3,
            "Ожидалось 3 группы демо-сида до DELETE, фактически " +
            $"[{string.Join(", ", namesBefore)}].");

        // when: DELETE /api/v1/groups/<uuid>.
        using var response = await B16GroupsApi.DeleteGroupAsync(client, unknownId.ToString());

        // then: 404 'Группа не найдена'.
        using var body = await B16GroupsApi.ParseWithStatusAsync(
            response, HttpStatusCode.NotFound, $"DELETE /groups/{unknownId} (teacher)");
        B16GroupsApi.MessageIs(body.RootElement, NotFoundText);

        // then: хранилище групп не изменилось (перечень имён совпадает со снимком).
        var namesAfter = B16GroupsApi.StoredGroupNames(groupsRepo);
        Assert.True(
            namesBefore.SequenceEqual(namesAfter),
            "Ожидался неизменный перечень групп после 404 на DELETE несуществующей группы, было " +
            $"[{string.Join(", ", namesBefore)}], стало [{string.Join(", ", namesAfter)}].");
    }
}
