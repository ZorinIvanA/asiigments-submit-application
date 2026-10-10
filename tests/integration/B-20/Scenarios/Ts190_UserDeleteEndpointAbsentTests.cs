using System.Net;
using System.Text.Json;
using LabsApp.IntegrationTests.B20.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-190 «Scope: API удаления пользователей отсутствует»
/// (scope, P2; OUT-SCOPE-USER-DELETION, SEC-004).
/// given: Приложение запущено; uuid существующего студента известен; сессия
///        teacher.
/// when:  DELETE /api/v1/users/{uuid}.
/// then:  404 {'message':'Не найдено'} — механизма удаления/анонимизации
///        учётных записей в API нет (out_of_scope: «Удаление учётных записей…»;
///        SEC-004). Контроль эффекта: учётная запись остаётся в хранилище.
/// </summary>
public sealed class Ts190_UserDeleteEndpointAbsentTests : IClassFixture<B20ApiFactory>
{
    private readonly B20ApiFactory _factory;

    public Ts190_UserDeleteEndpointAbsentTests(B20ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteUser_WithTeacherSession_Returns404Envelope_UserIntact()
    {
        // given: приложение запущено; uuid существующего студента (DI-сид);
        // сессия teacher.
        var student = B20DomainSeed.AddStudent(_factory, "ts190-scope-student");
        var (teacherClient, _) = B20AuthSessions.CreateTeacherSession(_factory);
        using var client = teacherClient;

        // when: DELETE /api/v1/users/{uuid}.
        using var response = await client.DeleteAsync($"/api/v1/users/{student.Id}");

        // then: 404 {'message':'Не найдено'} — механизма удаления/анонимизации
        // учётных записей в API нет (конверт ровно с одним свойством message:
        // маршрут под /api не сопоставлен ни одному эндпойнту — FR-023).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType,
            ignoreCase: true);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        var properties = body.EnumerateObject().ToList();
        Assert.True(
            properties.Count == 1 && properties[0].Name == "message",
            "then не выполнен: тело ответа DELETE /api/v1/users/{uuid} — не единый "
            + "конверт {'message':'Не найдено'}: " + body.GetRawText());
        Assert.Equal("Не найдено", properties[0].Value.GetString());

        // then (контроль эффекта): учётная запись не удалена и не анонимизирована.
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetById(student.Id));
    }
}
