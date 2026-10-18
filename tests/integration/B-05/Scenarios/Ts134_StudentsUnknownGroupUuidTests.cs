using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-134 «Студенты: неизвестный uuid группы — пустая выборка, не 404»
/// (boundary, FR-020).
///
/// given: группы с указанным uuid не существует.
/// when:  GET /api/v1/students?groupId=&lt;произвольный uuid&gt;
/// then:  200; items=[], total=0 — НЕ 404 (FR-020 «неизвестный uuid — пустая
///        выборка, НЕ 404»).
/// </summary>
public sealed class Ts134_StudentsUnknownGroupUuidTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts134_StudentsUnknownGroupUuidTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownGroupUuid_ReturnsEmptySelectionInsteadOf404()
    {
        // given: uuid, которого нет среди сид-групп; teacher авторизован.
        var unknownGroupId = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр по неизвестной группе.
        using var response = await client.GetAsync($"/api/v1/students?groupId={unknownGroupId}");

        // then: 200 с пустой выборкой (не 404).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
