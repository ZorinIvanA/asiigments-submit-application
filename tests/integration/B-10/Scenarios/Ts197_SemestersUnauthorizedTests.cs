using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-197 (P1, negative; FR-035, FR-011) «GET /semesters без сессии: 401».
/// given: Cookie отсутствуют; в хранилище есть лабораторные (DI-сид, ADR-010).
/// when: GET /api/v1/semesters.
/// then: HTTP 401 {"message":"Не авторизован"} — FR-035 description: «доступен любой
///       авторизованной роли; без сессии — 401» (детерминированное тело FR-011).
/// </summary>
public sealed class Ts197_SemestersUnauthorizedTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS197_GetSemesters_WithoutCookie_Returns401UnauthorizedMessage()
    {
        // given: cookie отсутствуют (клиент без Cookie-заголовка); лабораторные в хранилище есть (DI-сид).
        B10Seed.AddLab(_factory, semester: 1, number: 1);
        Assert.True(
            B10Seed.TotalLabCount(_factory) > 0,
            "Шаг given неисполним: в хранилище нет лабораторных.");
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/semesters без сессии.
        using var response = await client.GetAsync("/api/v1/semesters");

        // then: 401 {"message":"Не авторизован"}.
        await ApiAssert.AssertMessageAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Не авторизован",
            exactSingleMessageProperty: true);
    }
}
