using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-161 (P0, negative; FR-062) «GET /me/submissions: teacher → 403, без сессии → 401».
/// given: Сессия teacher; отдельно запрос без cookie.
/// when: GET /api/v1/me/submissions?semester=1.
/// then: teacher → 403; без сессии → 401. FR-062 AC «Роль».
/// </summary>
public sealed class Ts161_MeSubmissionsRoleAndAuthTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS161_MeSubmissions_TeacherSession_Returns403Forbidden()
    {
        // given: сессия teacher.
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /me/submissions?semester=1.
        using var response = await client.GetAsync("/api/v1/me/submissions?semester=1");

        // then: 403 «Доступ запрещён».
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Forbidden, ErrorTexts.Forbidden);
    }

    [Fact]
    public async Task TS161_MeSubmissions_WithoutSession_Returns401Unauthorized()
    {
        // given: запрос без cookie (сессии нет).
        using var client = HostClients.Create(_factory);

        // when: GET /me/submissions?semester=1.
        using var response = await client.GetAsync("/api/v1/me/submissions?semester=1");

        // then: 401 «Не авторизован».
        await ApiAssert.AssertMessageAsync(response, HttpStatusCode.Unauthorized, ErrorTexts.Unauthorized);
    }
}
