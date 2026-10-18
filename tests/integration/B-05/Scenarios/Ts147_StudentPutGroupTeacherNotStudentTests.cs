using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-147 (P0, negative; FR-020 AC «Преподаватель не студент») «Студенты:
/// назначение преподавателю — 404 'Студент не найден'».
/// given: пользователь с указанным id существует с ролью teacher (сид-преподаватель).
/// when:  PUT /students/{teacherId}/group {groupId:null}
/// then:  404 'Студент не найден' (роль ≠ student).
/// </summary>
public sealed class Ts147_StudentPutGroupTeacherNotStudentTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts147_StudentPutGroupTeacherNotStudentTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupForTeacher_Returns404StudentNotFound()
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
}
