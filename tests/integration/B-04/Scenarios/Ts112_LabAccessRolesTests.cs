using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-112 (P0, negative; FR-017, FR-022) «Лабораторные: роли — student 403,
/// анонимно 401».
/// given: существуют валидные сессии student01 и teacher; возможен анонимный
///        запрос.
/// when:  GET /labs под student; GET /labs анонимно.
/// then:  403 'Доступ запрещён' и 401 'Не авторизован' соответственно.
/// </summary>
public sealed class Ts112_LabAccessRolesTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS112_GetLabsUnderStudentAndAnonymous_ForbiddenAndUnauthorized()
    {
        // given: валидная сессия student01 (DI-сид записи + минт access-JWT).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var student01 = B04DomainSeed.AddStudent(users, "student01");
        using var studentClient = MintedSessions.CreateStudentClient(_factory, student01);

        // when/then: GET /labs под student01 → 403 'Доступ запрещён'.
        using var forbidden = await studentClient.GetAsync("/api/v1/labs");
        await ApiAssert.AssertMessageAsync(
            forbidden, HttpStatusCode.Forbidden, "Доступ запрещён");

        // when/then: GET /labs анонимно → 401 'Не авторизован'.
        using var anonymous = MintedSessions.Create(_factory);
        using var unauthorized = await anonymous.GetAsync("/api/v1/labs");
        await ApiAssert.AssertMessageAsync(
            unauthorized, HttpStatusCode.Unauthorized, "Не авторизован");
    }
}
