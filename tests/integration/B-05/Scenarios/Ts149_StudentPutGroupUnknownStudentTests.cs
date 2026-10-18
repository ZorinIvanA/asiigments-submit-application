using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-149 (P1, negative; FR-020) «Студенты: несуществующий студент — 404».
/// given: случайный uuid студента; валидный uuid группы (ИК-223); сессия teacher.
/// when:  PUT /students/&lt;uuid&gt;/group {groupId:&lt;ИК-223.id&gt;}
/// then:  404 'Студент не найден'.
/// </summary>
public sealed class Ts149_StudentPutGroupUnknownStudentTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts149_StudentPutGroupUnknownStudentTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutGroupForUnknownStudent_Returns404StudentNotFound()
    {
        // given: случайный uuid студента; существующая группа ИК-223; teacher авторизован.
        var unknownStudentId = Guid.NewGuid();
        var ik223 = B05SeedLookup.GroupByName(_factory, "ИК-223");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: назначение группы несуществующему студенту.
        using var put = await client.PutAsJsonAsync(
            $"/api/v1/students/{unknownStudentId}/group",
            new { groupId = ik223.Id.ToString() });

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(put);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }
}
