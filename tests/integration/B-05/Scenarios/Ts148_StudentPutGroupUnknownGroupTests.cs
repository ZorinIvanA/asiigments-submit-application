using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-148 (P0, negative; FR-020) «Студенты: несуществующая группа в назначении —
/// 404 'Группа не найдена'».
/// given: student31 существует; groupId — случайный uuid; сессия teacher.
/// when:  PUT /students/{id31}/group {groupId:&lt;uuid&gt;}
/// then:  404 'Группа не найдена'.
/// </summary>
public sealed class Ts148_StudentPutGroupUnknownGroupTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts148_StudentPutGroupUnknownGroupTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutUnknownGroupForStudent_Returns404GroupNotFound()
    {
        // given: student31 существует; группы с указанным uuid нет; teacher авторизован.
        var student31 = B05SeedLookup.StudentByLogin(_factory, "student31");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: назначение student31 в несуществующую группу.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{student31.Id}/group",
            new { groupId = Guid.NewGuid().ToString() });

        // then: 404 'Группа не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(put);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }
}
