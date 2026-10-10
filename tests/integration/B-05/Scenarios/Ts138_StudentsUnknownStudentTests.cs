using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-138 «Студенты: несуществующий студент — 404» (negative, FR-020).
///
/// given: пользователя с указанным uuid не существует.
/// when:  PUT /api/v1/students/&lt;произвольный uuid&gt;/group {groupId:null}
/// then:  404 'Студент не найден' (FR-020 «id не найден → 404»).
/// </summary>
public sealed class Ts138_StudentsUnknownStudentTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts138_StudentsUnknownStudentTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupForUnknownStudent_ReturnsStudentNotFound()
    {
        // given: uuid, которого нет среди пользователей; teacher авторизован.
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
}
