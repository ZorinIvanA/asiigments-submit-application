using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-112 (P0, happy_path; FR-020 AC «Исключение из группы») «Студенты:
/// исключение из группы (groupId=null)».
/// given: студент состоит в группе (student01 в ИК-221 демо-сида); сессия teacher.
/// when:  PUT /students/{id}/group {groupId:null}
/// then:  204; groupId=null (студент виден в /students?groupId=none).
/// </summary>
public sealed class Ts112_StudentExcludeGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts112_StudentExcludeGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupNull_RemovesStudentFromGroup()
    {
        // given: student01 состоит в группе (ИК-221 демо-сида); teacher авторизован.
        var student01 = B05SeedLookup.StudentByLogin(_factory, "student01");
        Assert.NotNull(student01.GroupId);
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: исключение из группы (groupId:null).
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{student01.Id}/group",
            new { groupId = (string?)null });

        // then: 204; студент виден в /students?groupId=none с groupId=null.
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var none = await client.GetAsync("/api/v1/students?groupId=none");
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(none);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        var entry = items.SingleOrDefault(item =>
            string.Equals(item.GetProperty("login").GetString(), "student01", StringComparison.Ordinal));
        Assert.True(
            entry.ValueKind == JsonValueKind.Object,
            "Студент student01 не найден в /students?groupId=none после исключения из группы.");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("groupName").ValueKind);
    }
}
