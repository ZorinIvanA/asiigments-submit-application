using System.Text;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-126 (P0, negative; FR-022 + FR-011/FR-015..FR-021) «Матрица ролей:
/// анонимно/чужой ролью/своей ролью по классам доступа FR-022».
/// given: Развёрнут сид (DI-сид группы и студентов, ADR-010); доступны сессии
///        teacher, student (минт, ADR-022) и анонимные запросы. Операции разбиты
///        по классам доступа FR-022: (а) role=user — /auth/me, /me/profile
///        GET/PUT, /me/password PUT, /semesters; (б) teacher-only — /labs
///        GET/POST, /labs/{id} GET/PUT/DELETE, /groups GET/POST, /groups/{id}
///        PUT/DELETE, /groups/{id}/students GET, /students GET,
///        /students/{id}/group PUT, /submissions GET/PUT; (в) student-only —
///        /me/submissions GET.
/// when:  Для каждой операции класса (а) — вызов анонимно и обеими ролями;
///        класса (б) — анонимно, под student и под teacher; класса (в) —
///        анонимно, под teacher и под student.
/// then:  Анонимно — 401 'Не авторизован'. (а): под student и под teacher —
///        не 401/403. (б): под student — 403 'Доступ запрещён', под teacher —
///        не 401/403. (в): под teacher — 403, под student — не 401/403.
///        Порядок отказов 401→403→400→404→409 (проверки 401/403 дословные;
///        «своей ролью» статус по бизнес-правилам — тела PUT-вызовов с пустыми
///        объектами дают 400-ветки, не меняющие матрицу).
/// </summary>
public sealed class Ts126_RoleMatrixAccessClassTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    /// <summary>Операция матрицы: метка, метод, путь и (опционально) JSON-тело.</summary>
    private sealed record MatrixOperation(string Label, HttpMethod Method, string Path, string? JsonBody);

    [Fact]
    public async Task TS126_ClassA_UserEndpoints_Anonymous401_AnyRoleAllowed()
    {
        // given: сессии student и teacher (role=user — обе роли авторизованы).
        B10Seed.AddStudent(_factory, "b10ts126a.student");
        using var anonymous = HostClients.Create(_factory);
        using var student = HostClients.CreateStudentClient(_factory, "b10ts126a.student");
        using var teacher = HostClients.CreateTeacherClient(_factory);

        var operations = new[]
        {
            new MatrixOperation("GET /auth/me", HttpMethod.Get, "/api/v1/auth/me", null),
            new MatrixOperation("GET /me/profile", HttpMethod.Get, "/api/v1/me/profile", null),
            new MatrixOperation("PUT /me/profile", HttpMethod.Put, "/api/v1/me/profile", "{}"),
            new MatrixOperation("PUT /me/password", HttpMethod.Put, "/api/v1/me/password", "{}"),
            new MatrixOperation("GET /semesters", HttpMethod.Get, "/api/v1/semesters", null),
        };

        foreach (var operation in operations)
        {
            // when: анонимно и обеими ролями.
            await AssertUnauthorizedAsync(anonymous, operation);
            await AssertAllowedAsync(student, operation);
            await AssertAllowedAsync(teacher, operation);
        }
    }

    [Fact]
    public async Task TS126_ClassB_TeacherOnlyEndpoints_Anonymous401_Student403_TeacherAllowed()
    {
        // given: сессии teacher и student; известны uuid студента и группы,
        // несуществующий uuid работы.
        B10Seed.AddStudent(_factory, "b10ts126b.student");
        var studentId = B10Seed.AddStudent(_factory, "b10ts126b.target").Id;
        var groupId = B10Seed.AddGroup(_factory, "Б10-ТС126-Б").Id;
        var unknownLabId = Guid.NewGuid();
        using var anonymous = HostClients.Create(_factory);
        using var student = HostClients.CreateStudentClient(_factory, "b10ts126b.student");
        using var teacher = HostClients.CreateTeacherClient(_factory);

        var operations = new[]
        {
            new MatrixOperation("GET /labs", HttpMethod.Get, "/api/v1/labs", null),
            new MatrixOperation("POST /labs", HttpMethod.Post, "/api/v1/labs", "{}"),
            new MatrixOperation("GET /labs/{id}", HttpMethod.Get, $"/api/v1/labs/{unknownLabId}", null),
            new MatrixOperation("PUT /labs/{id}", HttpMethod.Put, $"/api/v1/labs/{unknownLabId}", "{\"semester\":0}"),
            new MatrixOperation("DELETE /labs/{id}", HttpMethod.Delete, $"/api/v1/labs/{unknownLabId}", null),
            new MatrixOperation("GET /groups", HttpMethod.Get, "/api/v1/groups", null),
            new MatrixOperation("POST /groups", HttpMethod.Post, "/api/v1/groups", "{\"name\":\"Б10-ТС126-Б-новая\"}"),
            new MatrixOperation("PUT /groups/{id}", HttpMethod.Put, $"/api/v1/groups/{groupId}", "{\"name\":\"Б10-ТС126-Б-переименована\"}"),
            new MatrixOperation("DELETE /groups/{id}", HttpMethod.Delete, $"/api/v1/groups/{groupId}", null),
            new MatrixOperation("GET /groups/{id}/students", HttpMethod.Get, $"/api/v1/groups/{groupId}/students", null),
            new MatrixOperation("GET /students", HttpMethod.Get, "/api/v1/students", null),
            new MatrixOperation("PUT /students/{id}/group", HttpMethod.Put, $"/api/v1/students/{studentId}/group", "{\"groupId\":null}"),
            new MatrixOperation("GET /submissions", HttpMethod.Get, "/api/v1/submissions", null),
            new MatrixOperation("PUT /submissions", HttpMethod.Put, "/api/v1/submissions", "{}"),
        };

        foreach (var operation in operations)
        {
            // when: анонимно, под student (чужая роль) и под teacher (своя роль).
            await AssertUnauthorizedAsync(anonymous, operation);
            await AssertForbiddenAsync(student, operation);
            await AssertAllowedAsync(teacher, operation);
        }
    }

    [Fact]
    public async Task TS126_ClassC_StudentOnlyEndpoints_Anonymous401_Teacher403_StudentAllowed()
    {
        // given: сессии student и teacher.
        B10Seed.AddStudent(_factory, "b10ts126c.student");
        using var anonymous = HostClients.Create(_factory);
        using var student = HostClients.CreateStudentClient(_factory, "b10ts126c.student");
        using var teacher = HostClients.CreateTeacherClient(_factory);

        var operations = new[]
        {
            new MatrixOperation("GET /me/submissions", HttpMethod.Get, "/api/v1/me/submissions?semester=1", null),
        };

        foreach (var operation in operations)
        {
            // when: анонимно, под teacher (чужая роль) и под student (своя роль).
            await AssertUnauthorizedAsync(anonymous, operation);
            await AssertForbiddenAsync(teacher, operation);
            await AssertAllowedAsync(student, operation);
        }
    }

    /// <summary>then «анонимно — 401 'Не авторизован'» (порядок FR-022: 401 первым).</summary>
    private static async Task AssertUnauthorizedAsync(HttpClient client, MatrixOperation operation)
    {
        using var response = await SendAsync(client, operation);
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"[{operation.Label} анонимно]: ожидался 401 «Не авторизован», фактически {(int)response.StatusCode}.");
        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Не авторизован", root.GetProperty("message").GetString());
    }

    /// <summary>then «чужой ролью — 403 'Доступ запрещён'» (403 после 401, до 400/404).</summary>
    private static async Task AssertForbiddenAsync(HttpClient client, MatrixOperation operation)
    {
        using var response = await SendAsync(client, operation);
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"[{operation.Label} чужой ролью]: ожидался 403 «Доступ запрещён», фактически {(int)response.StatusCode}.");
        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Доступ запрещён", root.GetProperty("message").GetString());
    }

    /// <summary>then «своей ролью — не 401/403» (дальнейший статус по бизнес-правилам).</summary>
    private static async Task AssertAllowedAsync(HttpClient client, MatrixOperation operation)
    {
        using var response = await SendAsync(client, operation);
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
            $"[{operation.Label} своей ролью]: статус не должен быть 401/403, фактически {(int)response.StatusCode}.");
    }

    /// <summary>Вызов матрицы: метод и путь операции, JSON-тело при наличии.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, MatrixOperation operation)
    {
        using var request = new HttpRequestMessage(operation.Method, operation.Path);
        if (operation.JsonBody is not null)
        {
            request.Content = new StringContent(operation.JsonBody, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }
}
