using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-113 (P0, negative; FR-020 AC «Преподаватель не студент») «Студенты:
/// 404 для не-студента, неизвестного id и неизвестной группы».
/// given: сессия teacher; известен uuid пользователя teacher и uuid несуществующей группы.
/// when:  PUT /students/{teacherId}/group {groupId:null};
///        PUT /students/&lt;несуществующий-id&gt;/group {groupId:null};
///        PUT /students/{id31}/group {groupId:&lt;несуществующий-uuid&gt;}.
/// then:  404 'Студент не найден'; 404 'Студент не найден'; 404 'Группа не найдена'.
/// </summary>
public sealed class Ts113_StudentGroup404Tests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts113_StudentGroup404Tests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupForTeacher_ReturnsStudentNotFound()
    {
        // given: существующий пользователь с ролью teacher; сессия teacher.
        var teacher = B05SeedLookup.Teacher(_factory);
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: попытка назначить группу пользователю-преподавателю.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{teacher.Id}/group",
            new { groupId = (string?)null });

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(put);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task PutGroupForUnknownStudent_ReturnsStudentNotFound()
    {
        // given: uuid, которого нет среди пользователей; сессия teacher.
        var unknownStudentId = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: назначение группы несуществующему студенту.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{unknownStudentId}/group",
            new { groupId = (string?)null });

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(put);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task PutUnknownGroupForStudent_ReturnsGroupNotFound()
    {
        // given: student31 существует; группы с указанным uuid нет; сессия teacher.
        var student31 = B05SeedLookup.StudentByLogin(_factory, "student31");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: назначение в несуществующую группу.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{student31.Id}/group",
            new { groupId = Guid.NewGuid().ToString() });

        // then: 404 'Группа не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(put);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }
}
