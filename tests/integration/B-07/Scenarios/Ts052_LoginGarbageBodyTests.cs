using System.Text;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-052 «Мусорное тело логина: 400 конверта» (negative, FR-007, P1).
///
/// given: пользователь teacher существует; неуспешных попыток в окне нет.
/// when:  POST /auth/login с телом 'not-json{{' (Content-Type: application/json).
/// then:  400 {'message':'Данные заполнены неверно'} — «синтаксически некорректный
///        JSON тела → 400» (IF-001; FR-007/ADR-014/ADR-010: битый JSON отличим
///        от отсутствующих полей, Δkdf=0; гейт
///        Login_MalformedJson_Returns400_WithoutKdf в LabsApp.Tests).
/// </summary>
public sealed class Ts052_LoginGarbageBodyTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts052_LoginGarbageBodyTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NotJsonBody_Returns400Envelope()
    {
        // given: пользователь teacher существует; неуспешных попыток в окне нет.
        using var client = B07AuthClients.CreateClient(_factory);

        // when: POST /auth/login с телом 'not-json{{' (Content-Type: application/json).
        using var content = new StringContent("not-json{{", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(B07AuthClients.LoginEndpoint, content);

        // then: 400 с сообщением конверта о некорректных данных.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
    }
}
