using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-155 (P1, scope; FR-022) «Scope: отсутствие эндпойнтов удаления пользователей
/// и смены роли».
/// given: Хост запущен; сессия teacher (минт, ADR-015); uuid студента известен
///        (DI-сид).
/// when:  DELETE /api/v1/users/&lt;uuid&gt;; PUT /api/v1/users/&lt;uuid&gt;/role
///        {role:'teacher'}.
/// then:  Оба — 404 {'message':'Не найдено'}: механизмы удаления/анонимизации
///        пользователей и управления ролями в API отсутствуют (out_of_scope;
///        SEC-004) — неизвестный маршрут под /api отвечает конвертом route-404.
/// </summary>
public sealed class Ts155_UserManagementEndpointsAbsentTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS155_UserDelete_AndRoleChange_ReturnRoute404()
    {
        // given: хост запущен; сессия teacher; uuid студента известен.
        var student = B11AuthSessions.SeedStudent(
            _factory, "b11ts155.student", "Пользователь Сто Пятьдесят Пять");
        using var teacher = B11AuthSessions.CreateSessionClient(
            _factory, SeedOptions.DefaultTeacherLogin);

        // when: DELETE /api/v1/users/<uuid>; PUT /api/v1/users/<uuid>/role.
        using var delete = await B11ApiCalls.SendAsync(
            teacher, HttpMethod.Delete, $"/api/v1/users/{student.Id}");
        using var putRole = await B11ApiCalls.SendAsync(
            teacher, HttpMethod.Put, $"/api/v1/users/{student.Id}/role", "{\"role\":\"teacher\"}");

        // then: оба — 404 {'message':'Не найдено'} (эндпойнтов нет — out_of_scope).
        _ = await ApiAssert.AssertMessageAsync(
            delete,
            HttpStatusCode.NotFound,
            "Не найдено",
            exactSingleMessageProperty: true);
        _ = await ApiAssert.AssertMessageAsync(
            putRole,
            HttpStatusCode.NotFound,
            "Не найдено",
            exactSingleMessageProperty: true);
    }
}
