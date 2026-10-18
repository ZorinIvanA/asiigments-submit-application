using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-144 (P1, boundary; FR-020) «Студенты: неизвестный uuid группы — пустая
/// выборка, не 404».
/// given: случайный uuid группы (в хранилище групп его нет — проверка по шву).
/// when:  GET /students?groupId=&lt;uuid&gt;
/// then:  200 {items:[], total:0} (НЕ 404).
/// </summary>
public sealed class Ts144_StudentsUnknownGroupUuidEmptySelectionTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts144_StudentsUnknownGroupUuidEmptySelectionTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownGroupUuid_Returns200EmptySelectionNot404()
    {
        // given: случайный uuid, которого нет среди групп; teacher авторизован.
        var unknownGroupId = Guid.NewGuid();
        Assert.Null(_factory.Services.GetRequiredService<IGroupRepository>().GetById(unknownGroupId));
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр студентов по неизвестной группе.
        using var response = await client.GetAsync($"/api/v1/students?groupId={unknownGroupId}");

        // then: 200 {items:[], total:0} — НЕ 404.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
