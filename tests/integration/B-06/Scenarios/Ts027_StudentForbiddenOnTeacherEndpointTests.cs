using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-027 «Роль не подходит: student на teacher-эндпоинте получает 403» (негативный,
/// P0, FR-011 AC «Роль не подходит»).
///
/// given: действующая сессия student (минт ADR-022: DI-сид пользователя +
///        ITokenService.IssueAccessToken, access-cookie в контейнере клиента).
/// when:  GET /api/v1/labs (teacher-only).
/// then:  HTTP 403 {"message":"Доступ запрещён"}.
/// </summary>
public sealed class Ts027_StudentForbiddenOnTeacherEndpointTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts027_StudentForbiddenOnTeacherEndpointTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetLabsWithStudentSession_Returns403WithForbiddenEnvelope()
    {
        // given: действующая сессия student.
        using var client = TestSessions.CreateStudentSession(
            _factory,
            login: "ts027.student",
            email: "ts027@example.com").Client;

        // when: GET /api/v1/labs.
        using var response = await client.GetAsync(ApiRequests.LabsEndpoint);

        // then: HTTP 403 с телом {"message":"Доступ запрещён"} — ровно один ключ.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Доступ запрещён");
    }
}
