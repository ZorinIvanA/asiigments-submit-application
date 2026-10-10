using System.Text;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-197 (P2, scope; FR-022) «Не реализовано управление пользователями вне спеки:
/// удаление/смена роли» (в волне прежней нумерации — TS-155; имя файла сохранено).
/// given: Приложение запущено; сессия teacher (сид-учётка Seed__*); валидные uuid
///        (uuid студента — DI-сид, ADR-010).
/// when:  DELETE /api/v1/users/&lt;uuid&gt;; PUT /api/v1/users/&lt;uuid&gt;/role
///        {role:'teacher'}; POST /api/v1/users.
/// then:  Все — 404 {'message':'Не найдено'}: ресурсы не существуют (out_of_scope:
///        «Управление ролями», «Удаление учётных записей»); несопоставленный
///        маршрут под /api (любой метод) отвечает 404-конвертом (FR-023/IF-001).
/// </summary>
public sealed class Ts155_UserManagementEndpointsAbsentTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task TS197_DeleteUser_PutUserRole_PostUser_AreAbsent_Return404NotFound()
    {
        // given: сессия teacher; uuid DI-сид-студента.
        var studentId = B12Seed.EnsureStudent(
            _factory,
            login: "b12-ts155-student",
            fullName: "Скоуп Пользователей Тестович",
            email: "b12-ts155@t.local").Id;
        using var teacher = HostClients.CreateTeacherClient(_factory);

        // when: DELETE /api/v1/users/<uuid>.
        using var deleteUser = await teacher.DeleteAsync($"/api/v1/users/{studentId}");

        // when: PUT /api/v1/users/<uuid>/role {role:'teacher'}.
        using var putRole = await teacher.PutAsync(
            $"/api/v1/users/{studentId}/role",
            new StringContent("{\"role\":\"teacher\"}", Encoding.UTF8, "application/json"));

        // when: POST /api/v1/users (создание учётной записи вне спеки).
        using var postUser = await teacher.PostAsync(
            "/api/v1/users",
            new StringContent("{\"login\":\"b12-ts155-new\"}", Encoding.UTF8, "application/json"));

        // then: все — 404 {'message':'Не найдено'} (эндпойнтов нет в API).
        await ApiAssert.AssertMessageAsync(
            deleteUser,
            HttpStatusCode.NotFound,
            ErrorTexts.NotFound,
            exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            putRole,
            HttpStatusCode.NotFound,
            ErrorTexts.NotFound,
            exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            postUser,
            HttpStatusCode.NotFound,
            ErrorTexts.NotFound,
            exactSingleMessageProperty: true);
    }
}
