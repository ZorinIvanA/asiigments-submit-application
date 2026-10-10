using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-131 «Студенты: фильтр groupId=none» (happy_path, FR-020).
///
/// given: в сиде student31 и student32 без группы (демо-сид FR-004).
/// when:  GET /api/v1/students?groupId=none
/// then:  200; только студенты с groupId=null (student31, student32), у каждого
///        groupName=null (FR-020 AC «Фильтр none»).
/// </summary>
public sealed class Ts131_StudentsGroupNoneFilterTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts131_StudentsGroupNoneFilterTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GroupIdNone_ReturnsOnlyStudentsWithoutGroup()
    {
        // given: teacher; в сиде ровно student31 и student32 без группы.
        var expectedLogins = new[] { "student31", "student32" };
        foreach (var login in expectedLogins)
        {
            Assert.Null(B05SeedLookup.StudentByLogin(_factory, login).GroupId);
        }

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр по отсутствию группы.
        using var response = await client.GetAsync("/api/v1/students?groupId=none");

        // then: 200; только студенты без группы; groupName=null у каждого.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var actualLogins = items
            .Select(item => item.GetProperty("login").GetString()!)
            .OrderBy(login => login, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedLogins, actualLogins);
        foreach (var item in items)
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupId").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("groupName").ValueKind);
        }
    }
}
