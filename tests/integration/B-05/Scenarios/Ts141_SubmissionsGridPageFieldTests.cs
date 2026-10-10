using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-141 «Ведомость: поле page присутствует в ответе (AR-003)» (boundary, FR-021).
///
/// given: ИК-221, 25 студентов, семестр 1 (демо-сид FR-004).
/// when:  GET /api/v1/submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=2
/// then:  200; тело содержит поля students, labs, submissions, total И page=2 —
///        клиентское DTO-подмножество {students,labs,submissions,total} не
///        нарушено (FR-021 AC «Поле page присутствует в ответе»).
/// </summary>
public sealed class Ts141_SubmissionsGridPageFieldTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts141_SubmissionsGridPageFieldTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SecondPage_BodyKeepsClientFieldsAndEchoesNormalizedPage()
    {
        // given: ИК-221; teacher авторизован.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: вторая страница ведомости.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=1&page=2");

        // then: 200; поля клиентского DTO + серверное расширение page=2.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "students", "labs", "submissions", "total", "page");
        Assert.Equal(2, root.GetProperty("page").GetInt32());
    }
}
