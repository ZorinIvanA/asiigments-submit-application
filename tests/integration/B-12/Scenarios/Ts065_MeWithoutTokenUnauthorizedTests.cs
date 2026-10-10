using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-065 (P0, negative; FR-011, FR-022) «auth/me без токена — 401».
/// given: Запрос без cookie access_token.
/// when:  GET /auth/me без cookie.
/// then:  401; message «Не авторизован» (FR-011 AC «Без токена»).
/// </summary>
public sealed class Ts065_MeWithoutTokenUnauthorizedTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Me_WithoutAccessToken_IsUnauthorizedWithDeterminedMessage()
    {
        // given: запрос без cookie access_token (клиент без cookie-контейнера).
        using var client = B12AuthSessions.CreateClient(_factory);

        // when: GET /auth/me без cookie.
        using var response = await client.GetAsync(B12AuthEndpoints.Me);

        // then: 401 «Не авторизован».
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }
}
