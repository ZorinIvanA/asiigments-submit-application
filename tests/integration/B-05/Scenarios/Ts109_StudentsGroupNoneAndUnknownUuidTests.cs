using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-109 (P0, boundary; FR-020) «Студенты: фильтр groupId=none и неизвестный
/// uuid — пустая выборка».
/// given: student31 и student32 без группы; uuid несуществующей группы; сессия teacher.
/// when:  GET /students?groupId=none; GET /students?groupId=&lt;неизвестный-uuid&gt;.
/// then:  none — только студенты с groupId=null (student31, student32);
///        неизвестный uuid — 200 с items=[] и total=0, НЕ 404.
/// </summary>
public sealed class Ts109_StudentsGroupNoneAndUnknownUuidTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts109_StudentsGroupNoneAndUnknownUuidTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GroupIdNone_ReturnsOnlyStudentsWithoutGroup()
    {
        // given: в сиде ровно student31 и student32 без группы; teacher авторизован.
        foreach (var login in new[] { "student31", "student32" })
        {
            Assert.Null(B05SeedLookup.StudentByLogin(_factory, login).GroupId);
        }

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр по отсутствию группы.
        using var response = await client.GetAsync("/api/v1/students?groupId=none");

        // then: только студенты с groupId=null (student31, student32), у каждого
        // groupId=null и groupName=null.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(2, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var actualLogins = items
            .Select(item => item.GetProperty("login").GetString()!)
            .OrderBy(login => login, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "student31", "student32" }, actualLogins);
        foreach (var item in items)
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupId").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupName").ValueKind);
        }
    }

    [Fact]
    public async Task UnknownGroupUuid_ReturnsEmptySelectionNot404()
    {
        // given: uuid, которого нет среди сид-групп; teacher авторизован.
        var unknownGroupId = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр по неизвестной группе.
        using var response = await client.GetAsync($"/api/v1/students?groupId={unknownGroupId}");

        // then: 200 с items=[] и total=0 — НЕ 404.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
    }
}
