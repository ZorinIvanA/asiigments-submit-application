using LabsApp.IntegrationTests.B15.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-120 «Семестры: анонимно — 401» (negative, FR-018/FR-022, P0).
///
/// given: запрос без cookie access_token (клиент без заголовка Cookie; хост без
///        предварительного сида — авторизация проверяется до обращения к данным).
/// when:  GET /api/v1/semesters без токена.
/// then:  401; message 'Не авторизован' дословно (FR-018 AC «Требуется авторизация»,
///        FR-022 «Тело ошибок едино», IF-001: errors у 401 отсутствует).
/// </summary>
public sealed class Ts120_SemestersAnonymousUnauthorizedTests : IClassFixture<B15WebAppFactory>
{
    private readonly B15WebAppFactory _factory;

    public Ts120_SemestersAnonymousUnauthorizedTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Semesters_WithoutToken_Returns401UnauthorizedMessage()
    {
        // given: клиент без cookie access_token (HandleCookies=false, заголовка нет).
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // when: GET /api/v1/semesters без токена.
        using var response = await client.GetAsync(B15Harness.SemestersEndpoint);

        // then: 401; JSON {message:'Не авторизован'} дословно, без errors.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не авторизован");
        Assert.False(
            root.TryGetProperty("errors", out _),
            "Тело 401 не должно содержать errors (errors — только у 400 полевой валидации).");
    }
}
