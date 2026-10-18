using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-139 «Студенты: назначение в несуществующую группу — 404» (negative, FR-020).
///
/// given: student31 существует; группы с указанным uuid нет.
/// when:  PUT /api/v1/students/{id31}/group {groupId:&lt;произвольный uuid&gt;}
/// then:  404; message 'Группа не найдена' (FR-020 «groupId ≠ null и группа не
///        существует → 404»).
/// </summary>
public sealed class Ts139_StudentsUnknownTargetGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts139_StudentsUnknownTargetGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutUnknownGroupForStudent_ReturnsGroupNotFound()
    {
        // given: student31 существует; teacher авторизован.
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
