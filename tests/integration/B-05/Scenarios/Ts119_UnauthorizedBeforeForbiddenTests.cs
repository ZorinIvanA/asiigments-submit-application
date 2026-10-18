using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-119 «Порядок: 401 раньше 403 на teacher-only эндпойнте» (негативный, FR-024).
///
/// given: cookies отсутствуют; эндпойнт GET /api/v1/labs требует роль teacher.
/// when:  GET /api/v1/labs без авторизации.
/// then:  HTTP 401 {message:'Не авторизован'} (не 403): проверка сессии предшествует
///        проверке роли. FR-024 AC «Порядок 401 раньше 403».
/// </summary>
public sealed class Ts119_UnauthorizedBeforeForbiddenTests : IClassFixture<B05WebAppFactory>
{
    private readonly B05WebAppFactory _factory;

    public Ts119_UnauthorizedBeforeForbiddenTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetTeacherOnlyLabsWithoutCookies_Returns401Not403()
    {
        // given: cookies отсутствуют — новый клиент без входа (пустой CookieContainer).
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/labs (teacher-only) без авторизации.
        using var response = await client.GetAsync("/api/v1/labs");

        // then: HTTP 401 (не 403) с телом {message:'Не авторизован'}.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Не авторизован");
    }
}
