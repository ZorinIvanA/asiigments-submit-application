using System.Text;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-155 (P1, scope; FR-022) «Scope: отсутствие эндпойнтов удаления пользователей
/// и смены роли».
/// given: Хост запущен; сессия teacher (сид-учётка Seed__*); известен uuid студента
///        (DI-сид, ADR-010).
/// when:  DELETE /api/v1/users/&lt;uuid&gt;; PUT /api/v1/users/&lt;uuid&gt;/role
///        {role:'teacher'}.
/// then:  Оба — 404 {'message':'Не найдено'}: механизмы удаления/анонимизации
///        пользователей и управления ролями в API отсутствуют (out_of_scope,
///        SEC-004); несопоставленный маршрут под /api отвечает 404-конвертом
///        (FR-023/IF-001).
/// </summary>
public sealed class Ts155_UserManagementEndpointsAbsentTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS155_DeleteUser_AndPutUserRole_AreAbsent_Return404NotFound()
    {
        // given: сессия teacher; uuid DI-сид-студента.
        var studentId = B10Seed.AddStudent(_factory, "b10ts155.student").Id;
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when: DELETE /api/v1/users/<uuid>.
        using var deleteUser = await teacher.DeleteAsync($"/api/v1/users/{studentId}");

        // when: PUT /api/v1/users/<uuid>/role {role:'teacher'}.
        using var putRole = await teacher.PutAsync(
            $"/api/v1/users/{studentId}/role",
            new StringContent("{\"role\":\"teacher\"}", Encoding.UTF8, "application/json"));

        // then: оба — 404 {'message':'Не найдено'} (эндпойнтов нет в API).
        await ApiAssert.AssertMessageAsync(
            deleteUser,
            HttpStatusCode.NotFound,
            "Не найдено",
            exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            putRole,
            HttpStatusCode.NotFound,
            "Не найдено",
            exactSingleMessageProperty: true);
    }
}
