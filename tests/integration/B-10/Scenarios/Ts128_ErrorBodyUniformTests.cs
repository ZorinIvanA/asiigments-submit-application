using System.Text;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-128 (P1, negative; FR-022, FR-023) «Тело ошибок авторизации едино,
/// без stack trace».
/// given: Вызваны защищённые эндпойнты /api/v1/* в состоянии отказа авторизации
///        (анонимно — отказ 401; сессия student на teacher-only эндпойнте — 403).
/// when:  Инспекция тел ответов 401 и 403.
/// then:  JSON {message:'Не авторизован' | 'Доступ запрещён'}; ровно одно
///        свойство message, без stack trace и имён типов (FR-022 AC «Тело ошибок
///        едино», FR-023 конверт).
/// </summary>
public sealed class Ts128_ErrorBodyUniformTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS128_UnauthorizedAndForbidden_Bodies_AreSingleMessageWithoutStackTrace()
    {
        // given: анонимный клиент (отказ 401) и сессия student на teacher-only
        // эндпойнте (отказ 403).
        B10Seed.AddStudent(_factory, "b10ts128.student");
        using var anonymous = HostClients.Create(_factory);
        using var student = HostClients.CreateStudentClient(_factory, "b10ts128.student");

        // when: GET /api/v1/labs анонимно (401) и под student (403).
        using var unauthorized = await anonymous.GetAsync("/api/v1/labs");
        using var forbidden = await student.GetAsync("/api/v1/labs");

        // then: 401 — JSON ровно {message:'Не авторизован'}, без stack trace.
        var unauthorizedBody = await ApiAssert.AssertMessageAsync(
            unauthorized,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
        AssertNoStackTraceOrTypeNames(unauthorizedBody.GetRawText());

        // then: 403 — JSON ровно {message:'Доступ запрещён'}, без stack trace.
        var forbiddenBody = await ApiAssert.AssertMessageAsync(
            forbidden,
            HttpStatusCode.Forbidden,
            "Доступ запрещён",
            exactSingleMessageProperty: true);
        AssertNoStackTraceOrTypeNames(forbiddenBody.GetRawText());
    }

    /// <summary>then «без stack trace и имён типов»: маркеры стека/типов в теле отсутствуют.</summary>
    private static void AssertNoStackTraceOrTypeNames(string rawBody)
    {
        Assert.DoesNotContain("Exception", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", rawBody, StringComparison.OrdinalIgnoreCase);
    }
}
