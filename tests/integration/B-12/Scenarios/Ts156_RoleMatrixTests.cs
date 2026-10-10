using System.Net.Http.Json;
using System.Text;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-156 (P0, negative; FR-022) «Матрица ролей по всем защищённым эндпойнтам».
/// given: Развёрнут сид; доступны сессии teacher и student и анонимные запросы;
///        перечень защищённых операций: GET/POST/PUT/DELETE labs*, groups*,
///        students*, submissions (GET/PUT) [teacher]; me/submissions [student];
///        auth/me, me/profile (GET/PUT), me/password (PUT), semesters [любая роль].
/// when:  Для каждой защищённой операции — вызов анонимно, чужой ролью, своей ролью.
/// then:  Анонимно — 401 «Не авторизован»; чужой ролью — 403 «Доступ запрещён»;
///        своей ролью — не 401/403 (статус по бизнес-правилам) (FR-022 AC
///        «Матрица ролей»).
/// Каждая операция — отдельный тест-кейс Theory; тела запросов доводят вызов до
/// бизнес-ветки (валидные даты/поля), чтобы «своей ролью» статус определялся
/// бизнес-правилами, а не конвертом валидации.
/// </summary>
public sealed class Ts156_RoleMatrixTests(B12WebAppFactory factory) : IClassFixture<B12WebAppFactory>
{
    /// <summary>Гарантированно несуществующий uuid (для 404-веток «своей роли»).</summary>
    private const string UnknownId = "00000000-0000-0000-0000-0000000000ff";

    private const string LabJson =
        "{\"number\":901,\"semester\":1,\"content\":\"Проверка матрицы B-12\"," +
        "\"assignmentUrl\":null,\"defenseRequired\":false}";

    private readonly B12WebAppFactory _factory = factory;
    private readonly Guid _groupId = B12Seed.EnsureGroup(factory, "ИК-B12-Matrix").Id;
    private readonly Guid _studentId = B12Seed.EnsureStudent(
        factory,
        login: "b12-ts156-student",
        fullName: "Матрица Ролей Тестович",
        email: "b12-ts156@t.local").Id;

    /// <summary>Перечень защищённых операций матрицы: (имя, метод, путь, тело, роли).</summary>
    public static IEnumerable<object?[]> Operations()
    {
        // teacher-only: labs*
        yield return new object?[] { "labs.list", "GET", B12AuthEndpoints.Labs, null, "teacher" };
        yield return new object?[] { "labs.get", "GET", $"{B12AuthEndpoints.Labs}/{UnknownId}", null, "teacher" };
        yield return new object?[] { "labs.create", "POST", B12AuthEndpoints.Labs, LabJson, "teacher" };
        yield return new object?[] { "labs.update", "PUT", $"{B12AuthEndpoints.Labs}/{UnknownId}", LabJson, "teacher" };
        yield return new object?[] { "labs.delete", "DELETE", $"{B12AuthEndpoints.Labs}/{UnknownId}", null, "teacher" };

        // teacher-only: groups*
        yield return new object?[] { "groups.list", "GET", "/api/v1/groups", null, "teacher" };
        yield return new object?[] { "groups.create", "POST", "/api/v1/groups", "{\"name\":\"ИК-B12-Matrix\"}", "teacher" };
        yield return new object?[] { "groups.students", "GET", $"/api/v1/groups/{UnknownId}/students", null, "teacher" };
        yield return new object?[] { "groups.rename", "PUT", $"/api/v1/groups/{UnknownId}", "{\"name\":\"ИК-B12-Matrix-Renamed\"}", "teacher" };
        yield return new object?[] { "groups.delete", "DELETE", $"/api/v1/groups/{UnknownId}", null, "teacher" };

        // teacher-only: students*
        yield return new object?[] { "students.list", "GET", "/api/v1/students", null, "teacher" };
        yield return new object?[] { "students.setGroup", "PUT", $"/api/v1/students/{UnknownId}/group", "{\"groupId\":null}", "teacher" };

        // teacher-only: submissions (GET/PUT)
        yield return new object?[] { "submissions.grid", "GET", $"/api/v1/submissions?groupId={{GROUP_ID}}&semester=1", null, "teacher" };
        yield return new object?[] { "submissions.upsert", "PUT", "/api/v1/submissions",
            "{\"studentId\":\"{STUDENT_ID}\",\"labId\":\"" + UnknownId + "\",\"submitDate\":\"2026-09-01\",\"defenseDate\":null}", "teacher" };

        // student-only: me/submissions
        yield return new object?[] { "me.submissions", "GET", "/api/v1/me/submissions?semester=1", null, "student" };

        // любая роль: auth/me, me/profile (GET/PUT), me/password (PUT), semesters
        yield return new object?[] { "auth.me", "GET", B12AuthEndpoints.Me, null, "any" };
        yield return new object?[] { "me.profile.get", "GET", "/api/v1/me/profile", null, "any" };
        yield return new object?[] { "me.profile.update", "PUT", "/api/v1/me/profile",
            "{\"fullName\":\"Матрица B-12 {ROLE}\",\"email\":\"b12-mtx-{ROLE}@t.local\"}", "any" };
        yield return new object?[] { "me.password.update", "PUT", "/api/v1/me/password",
            "{\"currentPassword\":\"b12-Wrong-Current1!\",\"password\":\"b12-New-Password1!\"," +
            "\"confirmPassword\":\"b12-New-Password1!\"}", "any" };
        yield return new object?[] { "semesters.list", "GET", "/api/v1/semesters", null, "any" };
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Matrix_Anonymous401_WrongRole403_OwnRoleBusiness(
        string operationName,
        string method,
        string path,
        string? body,
        string roles)
    {
        // Анонимно — 401 «Не авторизован».
        using (var anonymous = B12AuthSessions.CreateClient(_factory))
        using (var response = await SendAsync(anonymous, method, path, body, anonymousRole: true))
        {
            await ApiAssert.AssertMessageAsync(
                response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
        }

        if (roles == "any")
        {
            // «Своей ролью» для общих эндпойнтов являются ОБЕ роли.
            foreach (var role in new[] { UserRoles.Teacher, UserRoles.Student })
            {
                using var client = ClientWithRole(role);
                using var response = await SendAsync(client, method, path, body, anonymousRole: false, callerRole: role);
                AssertStatusIsBusiness(operationName, role, response.StatusCode);
            }

            return;
        }

        var ownerRole = roles;
        var wrongRole = ownerRole == UserRoles.Teacher ? UserRoles.Student : UserRoles.Teacher;

        // Чужой ролью — 403 «Доступ запрещён».
        using (var wrong = ClientWithRole(wrongRole))
        using (var wrongResponse = await SendAsync(wrong, method, path, body, anonymousRole: false, callerRole: wrongRole))
        {
            await ApiAssert.AssertMessageAsync(
                wrongResponse, HttpStatusCode.Forbidden, ErrorTexts.Forbidden);
        }

        // Своей ролью — статус по бизнес-правилам (не 401/403).
        using (var owner = ClientWithRole(ownerRole))
        using (var ownerResponse = await SendAsync(owner, method, path, body, anonymousRole: false, callerRole: ownerRole))
        {
            AssertStatusIsBusiness(operationName, ownerRole, ownerResponse.StatusCode);
        }
    }

    private HttpClient ClientWithRole(string role)
    {
        var userId = role == UserRoles.Teacher
            ? _factory.Services.GetRequiredService<IUserRepository>()
                    .GetByLogin(SeedOptions.DefaultTeacherLogin)?.Id
                ?? throw new InvalidOperationException("Сид-преподаватель не найден.")
            : _studentId;
        return B12AuthSessions.CreateClientWithAccess(_factory, userId, role);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        string path,
        string? body,
        bool anonymousRole,
        string? callerRole = null)
    {
        var resolvedPath = path
            .Replace("{GROUP_ID}", _groupId.ToString())
            .Replace("{STUDENT_ID}", _studentId.ToString());
        using var request = new HttpRequestMessage(ToHttpMethod(method), resolvedPath);
        if (body is not null)
        {
            var resolvedBody = anonymousRole
                ? body
                : body.Replace("{ROLE}", callerRole ?? string.Empty);
            request.Content = new StringContent(resolvedBody, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static void AssertStatusIsBusiness(string operationName, string role, HttpStatusCode status) =>
        Assert.True(
            status != HttpStatusCode.Unauthorized && status != HttpStatusCode.Forbidden,
            $"{operationName}: вызов своей ролью «{role}» должен доходить до бизнес-правил " +
            $"(не 401/403); фактически {(int)status}.");

    private static HttpMethod ToHttpMethod(string method) => method switch
    {
        "GET" => HttpMethod.Get,
        "POST" => HttpMethod.Post,
        "PUT" => HttpMethod.Put,
        "DELETE" => HttpMethod.Delete,
        _ => throw new InvalidOperationException($"Неизвестный метод матрицы: {method}"),
    };
}
