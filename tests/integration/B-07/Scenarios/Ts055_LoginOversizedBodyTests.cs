using System.Net.Http.Headers;
using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B07.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-055 «Логин с большим телом некорректного JSON: 400 конверта, лимитер
/// не тронут» (negative, FR-007 + FR-004, P1).
///
/// given: —.
/// when:  POST /auth/login с телом 262145 байт нулевых байтов
///        (Content-Type: application/json).
/// then:  400 {'message':'Данные заполнены неверно'} — синтаксически некорректный
///        JSON тела (IF-001/FR-007/ADR-014). Специфичного 413-гейта в конвейере
///        НЕТ (ADR-006/ISS-016/OQ-004: сверхлимитные тела Kestrel отвечает
///        телом фреймворка ДО конвейера; лимит Kestrel по умолчанию ~30 МБ,
///        256 КБ им не превышается). Состояние лимитера входа не меняется:
///        400 обрабатывается до оценки учётных данных.
/// </summary>
public sealed class Ts055_LoginOversizedBodyTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts055_LoginOversizedBodyTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Body262145BytesOfGarbage_Returns400Envelope_LoginLimiterUntouched()
    {
        // given: — (свежая фикстура класса).
        using var client = B07AuthClients.CreateClient(_factory);

        // when: POST /auth/login с телом 262145 байт (Content-Length > 262144).
        using var content = new ByteArrayContent(new byte[262145]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(B07AuthClients.LoginEndpoint, content);

        // then: 400 {'message':'Данные заполнены неверно'} (конверт IF-001).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        // then: состояние лимитера входа не меняется — 400 до лимитера.
        Assert.Equal(
            0,
            _factory.Services.GetRequiredService<LoginFailureLimiter>().TrackedKeysCount);
    }
}
