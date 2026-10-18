using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-141 (P0, happy_path; FR-020 AC «Фильтр 'none'») «Студенты: фильтр
/// groupId=none — только без группы».
/// given: демо-сид: student31 и student32 без группы; сессия teacher.
/// when:  GET /students?groupId=none
/// then:  200; в выдаче только студенты с groupId=null (student31, student32);
///        total=2.
/// </summary>
public sealed class Ts141_StudentsGroupNoneOnlyWithoutGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts141_StudentsGroupNoneOnlyWithoutGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GroupIdNone_ReturnsExactlyStudentsWithoutGroup()
    {
        // given: в демо-сиде ровно student31 и student32 без группы; teacher авторизован.
        foreach (var login in new[] { "student31", "student32" })
        {
            Assert.Null(B05SeedLookup.StudentByLogin(_factory, login).GroupId);
        }

        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: фильтр по отсутствию группы.
        using var response = await client.GetAsync("/api/v1/students?groupId=none");

        // then: 200; только студенты с groupId=null (student31, student32);
        // у каждого groupId=null и groupName=null.
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
}
