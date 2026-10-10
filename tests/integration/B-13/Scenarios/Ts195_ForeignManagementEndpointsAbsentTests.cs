using System.Text;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B13.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-195 (P2, scope; FR-022, FR-002) «Scope: посторонние эндпойнты управления
/// отсутствуют». API не содержит удаления учётных записей, смены роли и
/// metrics-эндпойнта (out_of_scope: SEC-004, управление ролями, Prometheus).
///
/// given: Два стенда одного приложения: (а) каталог wwwroot пуст/отсутствует
///        (нет index.html), (б) wwwroot/index.html существует (маркерное
///        содержимое); известен uuid существующего студента.
/// when:  На обоих стендах: DELETE /api/v1/users/{id};
///        PUT /api/v1/users/{id}/role {role:'teacher'}; GET /metrics.
/// then:  На стенде (а) все три запроса — 404 с телом
///        {'message':'Не найдено'} (маршрут вне /api без index.html — FR-002 AC
///        «Нет index.html»); на стенде (б) маршруты /api/v1/users* — 404
///        {'message':'Не найдено'} (единый конверт FR-023), а GET /metrics —
///        200 text/html с телом = index.html (правило SPA fallback FR-002:
///        маршрут не к /api/*, не к /health и не к /swagger*), и ни в одном
///        случае — не JSON-метрики. Контроль эффекта: ни на одном стенде
///        пользователь не удалён и его роль не изменена (под teacher студент
///        виден в GET /students; GET /auth/me пользователя работает и роль
///        прежняя — student).
/// </summary>
public sealed class Ts195_ForeignManagementEndpointsAbsentTests
{
    private const string StudentLogin = "ts195-student";

    [Fact]
    public async Task TS195_StandA_NoIndexHtml_AllThreeRequests_Return404Envelope_NoSideEffects()
    {
        // given: стенд (а) — каталог wwwroot отсутствует (нет index.html);
        // известен uuid существующего студента (DI-сид).
        using var factory = new B13ScopeWebAppFactory(B13ScopeWebAppFactory.WwwrootMode.NoIndexHtml);
        var student = B13ScopeHost.AddStudent(factory, StudentLogin);
        using var client = B13ScopeHost.CreateClient(factory);

        // when: DELETE /api/v1/users/{id}; PUT /api/v1/users/{id}/role; GET /metrics.
        using var delete = await client.DeleteAsync($"/api/v1/users/{student.Id}");
        using var putRole = await client.PutAsync(
            $"/api/v1/users/{student.Id}/role",
            new StringContent("""{"role":"teacher"}""", Encoding.UTF8, "application/json"));
        using var metrics = await client.GetAsync("/metrics");

        // then: все три запроса — 404 с телом {'message':'Не найдено'}
        // (конверт ровно {message}; не JSON-метрики).
        await ApiAssert.AssertMessageAsync(
            delete, HttpStatusCode.NotFound, "Не найдено", exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            putRole, HttpStatusCode.NotFound, "Не найдено", exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            metrics, HttpStatusCode.NotFound, "Не найдено", exactSingleMessageProperty: true);

        // then: контроль эффекта на стенде (а) — пользователь не удалён и роль не изменена.
        await AssertStudentIntactAsync(factory, student.Id);
    }

    [Fact]
    public async Task TS195_StandB_IndexHtmlPresent_UsersRoutes404_MetricsServesIndexHtml_NoSideEffects()
    {
        // given: стенд (б) — wwwroot/index.html существует (маркерное содержимое);
        // известен uuid существующего студента (DI-сид).
        using var factory = new B13ScopeWebAppFactory(B13ScopeWebAppFactory.WwwrootMode.IndexHtmlPresent);
        var student = B13ScopeHost.AddStudent(factory, StudentLogin);
        using var client = B13ScopeHost.CreateClient(factory);

        // when: DELETE /api/v1/users/{id}; PUT /api/v1/users/{id}/role; GET /metrics.
        using var delete = await client.DeleteAsync($"/api/v1/users/{student.Id}");
        using var putRole = await client.PutAsync(
            $"/api/v1/users/{student.Id}/role",
            new StringContent("""{"role":"teacher"}""", Encoding.UTF8, "application/json"));
        using var metrics = await client.GetAsync("/metrics");

        // then: маршруты /api/v1/users* — 404 {'message':'Не найдено'}
        // (единый конверт FR-023; управление учётными записями/ролями отсутствует).
        await ApiAssert.AssertMessageAsync(
            delete, HttpStatusCode.NotFound, "Не найдено", exactSingleMessageProperty: true);
        await ApiAssert.AssertMessageAsync(
            putRole, HttpStatusCode.NotFound, "Не найдено", exactSingleMessageProperty: true);

        // then: GET /metrics — 200 text/html с телом = index.html (SPA fallback
        // FR-002: маршрут не к /api/*, не к /health и не к /swagger*), не JSON-метрики.
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
        Assert.Equal("text/html", metrics.Content.Headers.ContentType?.MediaType);
        Assert.Equal(factory.IndexHtmlBytes, await metrics.Content.ReadAsByteArrayAsync());

        // then: контроль эффекта на стенде (б) — пользователь не удалён и роль не изменена.
        await AssertStudentIntactAsync(factory, student.Id);
    }

    /// <summary>
    /// Контроль эффекта кейса: под teacher студент виден в GET /students
    /// (не удалён); GET /auth/me пользователя работает и роль прежняя — student.
    /// Сессии минтятся через ITokenService (ADR-022).
    /// </summary>
    private static async Task AssertStudentIntactAsync(B13ScopeWebAppFactory factory, Guid studentId)
    {
        using (var teacherClient = B13ScopeHost.CreateTeacherClient(factory))
        {
            using var students = await teacherClient.GetAsync(B13ScopeHost.StudentsEndpoint);
            var body = await ApiAssert.ReadOkJsonAsync(students);
            var items = body.GetProperty("items");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);
            var listed = items.EnumerateArray().Any(item =>
                string.Equals(
                    item.GetProperty("id").GetString(),
                    studentId.ToString(),
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(listed, "Контроль эффекта: студент не найден в GET /students — учётная запись удалена.");
        }

        using (var meClient = B13ScopeHost.CreateClient(factory))
        {
            B13ScopeHost.MintAccessCookie(factory, meClient, studentId, UserRoles.Student);
            using var me = await meClient.GetAsync(B13ScopeHost.MeEndpoint);
            var meBody = await ApiAssert.ReadOkJsonAsync(me);
            Assert.Equal(StudentLogin, meBody.GetProperty("login").GetString());
            Assert.Equal(UserRoles.Student, meBody.GetProperty("role").GetString());
        }
    }
}
