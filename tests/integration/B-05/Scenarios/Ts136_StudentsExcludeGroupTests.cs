using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-136 «Студенты: исключение из группы» (happy_path, FR-020).
///
/// given: студент состоит в группе (в сиде student01 входит в ИК-221).
/// when:  PUT /api/v1/students/{id}/group {groupId:null}
/// then:  204; в выдаче /students у студента groupId=null, groupName=null
///        (FR-020 AC «Исключение из группы»).
/// </summary>
public sealed class Ts136_StudentsExcludeGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts136_StudentsExcludeGroupTests(B05WebAppFactory factory) => _factory = factory;

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

        // then: 204; в выдаче /students у студента groupId=null и groupName=null.
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        using var list = await client.GetAsync("/api/v1/students?search=student01");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(list);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        var entry = items.SingleOrDefault(item =>
            string.Equals(item.GetProperty("login").GetString(), "student01", StringComparison.Ordinal));
        Assert.True(
            entry.ValueKind == JsonValueKind.Object,
            "Студент student01 не найден в выдаче /students после исключения из группы.");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("groupName").ValueKind);
    }
}
