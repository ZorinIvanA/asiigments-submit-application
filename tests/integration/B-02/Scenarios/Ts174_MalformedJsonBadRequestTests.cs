using LabsApp.IntegrationTests.B02.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-174 «Конверт ошибок: синтаксически некорректный JSON на бизнес-эндпойнте»
/// (FR-023, P1; тип negative).
///
/// given: teacher авторизован (Development-хост, штатный вход сеяного преподавателя
///        с дефолтным сид-паролем).
/// when:  POST /api/v1/groups с телом '{bad json' (Content-Type: application/json).
/// then:  400 {'message':'Данные заполнены неверно'} — единый конверт (FR-023);
///        ключа errors в теле нет (errors присутствует только у 400 полевой
///        валидации, IF-001).
/// </summary>
public sealed class Ts174_MalformedJsonBadRequestTests
{
    [Fact]
    public async Task PostGroups_WithMalformedJsonBody_ReturnsSingleEnvelopeWithoutErrors()
    {
        // given: Development-хост (харнес-умолчания) и авторизованный teacher.
        using var factory = new B02WebAppFactory();
        using var client = factory.CreateWarmClient();
        using var login = await HostClients.LoginAsDefaultTeacherAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // when: POST с синтаксически некорректным JSON-телом.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/groups")
        {
            Content = new StringContent("{bad json", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);

        // then: 400 с единым конвертом; errors отсутствует (не полевая валидация).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Данные заполнены неверно", payload.GetProperty("message").GetString());
        Assert.False(
            payload.TryGetProperty("errors", out _),
            "Ключ errors присутствует в теле 400 на некорректный JSON — errors допустим только "
            + "у 400 полевой валидации (FR-023/IF-001). Тело: " + body);
    }
}
