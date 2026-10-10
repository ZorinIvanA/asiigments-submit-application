using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-126 (P0, negative; FR-022, FR-011, FR-015..FR-021) «Матрица ролей».
/// given: DI-сид (демо-набор отключён, ADR-010); сессии student и teacher минтятся
///        (ADR-015), анонимные запросы — клиент без cookie. Операции по классам
///        доступа FR-022: (а) role=user — /auth/me, /me/profile GET/PUT,
///        /me/password PUT, /semesters; (б) teacher-only — /labs GET/POST,
///        /labs/{id} GET/PUT/DELETE, /groups GET/POST, /groups/{id} PUT/DELETE,
///        /groups/{id}/students GET, /students GET, /students/{id}/group PUT,
///        /submissions GET и PUT; (в) student-only — /me/submissions GET.
/// when:  Класс (а) — вызов анонимно и обеими ролями; класс (б) — анонимно, под
///        student и под teacher; класс (в) — анонимно, под teacher и под student.
/// then:  Во всех классах анонимно — 401 «Не авторизован». Класс (а): обе роли —
///        не 401/403 (чужой роли не существует). Класс (б): student — 403
///        «Доступ запрещён», teacher — не 401/403. Класс (в): teacher — 403,
///        student — не 401/403. Порядок отказов 401→403→400→404→409: анонимный
///        запрос получает 401, а не 403 (FR-022 AC «Матрица ролей»).
/// </summary>
public sealed class Ts126_RoleMatrixTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    /// <summary>Операция матрицы: имя, метод, путь, опциональное JSON-тело.</summary>
    private sealed record MatrixOp(string Name, HttpMethod Method, string Path, string? Json = null);

    /// <summary>Несуществующие uuid сущностей для 404-веток под своей ролью.</summary>
    private static readonly Guid UnknownLabId = new("00000000-0000-0000-0000-000000000126");
    private static readonly Guid UnknownGroupId = new("00000000-0000-0000-0000-000000000127");
    private static readonly Guid UnknownStudentId = new("00000000-0000-0000-0000-000000000128");

    /// <summary>Класс (а) FR-022 — role=user (любая аутентифицированная роль).</summary>
    private static readonly MatrixOp[] UserClassOps =
    [
        new("GET /auth/me", HttpMethod.Get, "/api/v1/auth/me"),
        new("GET /me/profile", HttpMethod.Get, "/api/v1/me/profile"),
        new("PUT /me/profile", HttpMethod.Put, "/api/v1/me/profile", "{}"),
        new("PUT /me/password", HttpMethod.Put, "/api/v1/me/password", "{}"),
        new("GET /semesters", HttpMethod.Get, "/api/v1/semesters"),
    ];

    /// <summary>Класс (б) FR-022 — teacher-only.</summary>
    private static readonly MatrixOp[] TeacherOnlyClassOps =
    [
        new("GET /labs", HttpMethod.Get, "/api/v1/labs"),
        new("POST /labs", HttpMethod.Post, "/api/v1/labs",
            "{\"number\":126,\"semester\":1,\"content\":\"Матрица ролей\"," +
            "\"assignmentUrl\":null,\"defenseRequired\":false}"),
        new("GET /labs/{id}", HttpMethod.Get, $"/api/v1/labs/{UnknownLabId}"),
        new("PUT /labs/{id}", HttpMethod.Put, $"/api/v1/labs/{UnknownLabId}",
            "{\"number\":126,\"semester\":0,\"content\":\"Матрица ролей\"," +
            "\"assignmentUrl\":\"\",\"defenseRequired\":false}"),
        new("DELETE /labs/{id}", HttpMethod.Delete, $"/api/v1/labs/{UnknownLabId}"),
        new("GET /groups", HttpMethod.Get, "/api/v1/groups"),
        new("POST /groups", HttpMethod.Post, "/api/v1/groups", "{\"name\":\"ИК-126\"}"),
        new("PUT /groups/{id}", HttpMethod.Put, $"/api/v1/groups/{UnknownGroupId}",
            "{\"name\":\"ИК-126-переименованная\"}"),
        new("DELETE /groups/{id}", HttpMethod.Delete, $"/api/v1/groups/{UnknownGroupId}"),
        new("GET /groups/{id}/students", HttpMethod.Get,
            $"/api/v1/groups/{UnknownGroupId}/students"),
        new("GET /students", HttpMethod.Get, "/api/v1/students"),
        new("PUT /students/{id}/group", HttpMethod.Put,
            $"/api/v1/students/{UnknownStudentId}/group", "{\"groupId\":null}"),
        new("GET /submissions", HttpMethod.Get, "/api/v1/submissions"),
        new("PUT /submissions", HttpMethod.Put, "/api/v1/submissions", "{}"),
    ];

    /// <summary>Класс (в) FR-022 — student-only.</summary>
    private static readonly MatrixOp[] StudentOnlyClassOps =
    [
        new("GET /me/submissions", HttpMethod.Get, "/api/v1/me/submissions?semester=1"),
    ];

    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS126_UserClass_Anonymous401_BothRolesNot401Or403()
    {
        // given: сессии student и teacher и анонимные запросы.
        B11AuthSessions.SeedStudent(_factory, "b11ts126.a.student", "Матрица Класс А");
        using var anonymous = HostClients.Create(_factory);
        using var student = B11AuthSessions.CreateSessionClient(_factory, "b11ts126.a.student");
        using var teacher = B11AuthSessions.CreateSessionClient(
            _factory, SeedOptions.DefaultTeacherLogin);

        // when: для каждой операции класса (а) — анонимно и обеими ролями.
        foreach (var op in UserClassOps)
        {
            using var anonymousResponse = await B11ApiCalls.SendAsync(anonymous, op.Method, op.Path, op.Json);
            using var studentResponse = await B11ApiCalls.SendAsync(student, op.Method, op.Path, op.Json);
            using var teacherResponse = await B11ApiCalls.SendAsync(teacher, op.Method, op.Path, op.Json);

            // then: анонимно — 401 «Не авторизован»; под student и teacher —
            // не 401/403 (дальнейший статус — по бизнес-правилам).
            await ApiAssert.AssertMessageAsync(
                anonymousResponse, HttpStatusCode.Unauthorized, "Не авторизован");
            AssertNotAccessDenied(studentResponse.StatusCode, $"класс (а), {op.Name}, student");
            AssertNotAccessDenied(teacherResponse.StatusCode, $"класс (а), {op.Name}, teacher");
        }
    }

    [Fact]
    public async Task TS126_TeacherOnlyClass_Anonymous401_Student403_TeacherAllowed()
    {
        // given: сессии student и teacher и анонимные запросы.
        B11AuthSessions.SeedStudent(_factory, "b11ts126.b.student", "Матрица Класс Б");
        using var anonymous = HostClients.Create(_factory);
        using var student = B11AuthSessions.CreateSessionClient(_factory, "b11ts126.b.student");
        using var teacher = B11AuthSessions.CreateSessionClient(
            _factory, SeedOptions.DefaultTeacherLogin);

        // when: для каждой операции класса (б) — анонимно, под student и под teacher.
        foreach (var op in TeacherOnlyClassOps)
        {
            using var anonymousResponse = await B11ApiCalls.SendAsync(anonymous, op.Method, op.Path, op.Json);
            using var studentResponse = await B11ApiCalls.SendAsync(student, op.Method, op.Path, op.Json);
            using var teacherResponse = await B11ApiCalls.SendAsync(teacher, op.Method, op.Path, op.Json);

            // then: анонимно — 401 «Не авторизован» (401 раньше 403); под student —
            // 403 «Доступ запрещён»; под teacher — не 401/403.
            await ApiAssert.AssertMessageAsync(
                anonymousResponse, HttpStatusCode.Unauthorized, "Не авторизован");
            await ApiAssert.AssertMessageAsync(
                studentResponse, HttpStatusCode.Forbidden, "Доступ запрещён");
            AssertNotAccessDenied(teacherResponse.StatusCode, $"класс (б), {op.Name}, teacher");
        }
    }

    [Fact]
    public async Task TS126_StudentOnlyClass_Anonymous401_Teacher403_StudentAllowed()
    {
        // given: сессии student и teacher и анонимные запросы.
        B11AuthSessions.SeedStudent(_factory, "b11ts126.c.student", "Матрица Класс В");
        using var anonymous = HostClients.Create(_factory);
        using var student = B11AuthSessions.CreateSessionClient(_factory, "b11ts126.c.student");
        using var teacher = B11AuthSessions.CreateSessionClient(
            _factory, SeedOptions.DefaultTeacherLogin);

        // when: для каждой операции класса (в) — анонимно, под teacher и под student.
        foreach (var op in StudentOnlyClassOps)
        {
            using var anonymousResponse = await B11ApiCalls.SendAsync(anonymous, op.Method, op.Path, op.Json);
            using var teacherResponse = await B11ApiCalls.SendAsync(teacher, op.Method, op.Path, op.Json);
            using var studentResponse = await B11ApiCalls.SendAsync(student, op.Method, op.Path, op.Json);

            // then: анонимно — 401 «Не авторизован»; под teacher — 403
            // «Доступ запрещён»; под student — не 401/403.
            await ApiAssert.AssertMessageAsync(
                anonymousResponse, HttpStatusCode.Unauthorized, "Не авторизован");
            await ApiAssert.AssertMessageAsync(
                teacherResponse, HttpStatusCode.Forbidden, "Доступ запрещён");
            AssertNotAccessDenied(studentResponse.StatusCode, $"класс (в), {op.Name}, student");
        }
    }

    /// <summary>then-проверка «не 401/403» — статус по бизнес-правилам (FR-022).</summary>
    private static void AssertNotAccessDenied(HttpStatusCode status, string context) =>
        Assert.True(
            status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
            $"{context}: ожидался статус по бизнес-правилам (не 401/403), фактически {(int)status}.");
}
