using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-128 (P1, negative; FR-022, FR-023) «Тело ошибок авторизации едино, без
/// stack trace».
/// given: Вызваны защищённые эндпойнты /api/v1/* в состоянии отказа авторизации
///        (401 — анонимный /auth/me; 403 — student против teacher-only /labs).
/// when:  Инспекция тел ответов 401 и 403.
/// then:  JSON {message:'Не авторизован' | 'Доступ запрещён'}; без stack trace и
///        имён типов (FR-022 AC «Тело ошибок едино»): единственное свойство
///        message, в тексте ответа нет служебных деталей.
/// </summary>
public sealed class Ts128_AuthErrorEnvelopeUniformTests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task TS128_AuthErrorBodies_AreUniformJsonMessageWithoutStackTrace()
    {
        // given: 401 — анонимный запрос /auth/me; 403 — student → teacher-only /labs.
        B11AuthSessions.SeedStudent(_factory, "b11ts128.student", "Конверт Ошибок");
        using var anonymous = HostClients.Create(_factory);
        using var student = B11AuthSessions.CreateSessionClient(_factory, "b11ts128.student");

        // when: инспекция тел ответов 401 и 403.
        using var unauthorized = await anonymous.GetAsync("/api/v1/auth/me");
        using var forbidden = await student.GetAsync("/api/v1/labs");

        // then: JSON {message: …} — единственное свойство, дословный текст словаря.
        _ = await ApiAssert.AssertMessageAsync(
            unauthorized,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
        _ = await ApiAssert.AssertMessageAsync(
            forbidden,
            HttpStatusCode.Forbidden,
            "Доступ запрещён",
            exactSingleMessageProperty: true);

        // then: без stack trace и имён типов (FR-023: без деталей исключения).
        AssertNoServiceDetails(await unauthorized.Content.ReadAsStringAsync(), "401");
        AssertNoServiceDetails(await forbidden.Content.ReadAsStringAsync(), "403");
    }

    /// <summary>В теле ошибки нет маркеров служебных деталей (stack trace/типы).</summary>
    private static void AssertNoServiceDetails(string body, string context)
    {
        foreach (var marker in new[] { "Exception", "stack", "at LabsApp", "System." })
        {
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
