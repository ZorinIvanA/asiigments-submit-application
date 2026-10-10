using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-137 «Студенты: преподаватель — не студент (404)» (negative, FR-020).
///
/// given: пользователь с указанным id существует с ролью teacher (uuid сидового
///        преподавателя — тот же, что вернул бы /auth/me; сессии и сущности кейса
///        берутся из DI, ADR-015 — без зависимости от эндпойнтов другой волны).
/// when:  PUT /api/v1/students/{teacherId}/group {groupId:null}
/// then:  404; message 'Студент не найден' (FR-020 AC «Преподаватель не студент»).
/// </summary>
public sealed class Ts137_StudentsTeacherNotStudentTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts137_StudentsTeacherNotStudentTests(B05WebAppFactory factory) => _factory = factory;

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
}
