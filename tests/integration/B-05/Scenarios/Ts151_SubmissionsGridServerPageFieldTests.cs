using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-151 (P0, boundary; FR-021 AC «Поле page присутствует в ответе», AR-003)
/// «Ведомость: серверное поле page присутствует».
/// given: ИК-221, 25 студентов, семестр 1 (демо-сид FR-004); сессия teacher.
/// when:  GET /submissions?groupId=&lt;ИК-221&gt;&amp;semester=1&amp;page=2
/// then:  200; тело содержит поля students, labs, submissions, total И page=2
///        (клиентское DTO — подмножество полей ответа).
/// </summary>
public sealed class Ts151_SubmissionsGridServerPageFieldTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts151_SubmissionsGridServerPageFieldTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SecondPage_BodyContainsClientFieldsAndPage2()
    {
        // given: ИК-221; teacher авторизован.
        var ik221 = B05SeedLookup.GroupByName(_factory, "ИК-221");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: вторая страница ведомости.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={ik221.Id}&semester=1&page=2");

        // then: 200; тело содержит students, labs, submissions, total И page=2
        // (клиентское DTO {students,labs,submissions,total} — подмножество полей ответа).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        Assert.True(root.TryGetProperty("students", out _), "В теле ведомости нет поля students.");
        Assert.True(root.TryGetProperty("labs", out _), "В теле ведомости нет поля labs.");
        Assert.True(root.TryGetProperty("submissions", out _), "В теле ведомости нет поля submissions.");
        Assert.True(root.TryGetProperty("total", out _), "В теле ведомости нет поля total.");
        Assert.True(root.TryGetProperty("page", out var page), "В теле ведомости нет поля page (AR-003).");
        Assert.Equal(2, page.GetInt32());
    }
}
