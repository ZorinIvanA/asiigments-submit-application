using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-100 (P0, negative; FR-017 AC «Роли», FR-022) «Лабораторные: роли —
/// student 403, анонимно 401».
/// given: существуют валидная сессия student и возможность анонимного запроса.
/// when:  GET /api/v1/labs под student; анонимно.
/// then:  403 'Доступ запрещён' и 401 'Не авторизован' соответственно
///        (оба — JSON-конверт).
/// </summary>
public sealed class Ts100_LabRolesTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS100_GetLabsUnderStudentAndAnonymous_ForbiddenAndUnauthorized()
    {
        // given: валидная сессия student (DI-сид записи + минт access-JWT).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var student = B04DomainSeed.AddStudent(users, "b04-ts100-student");
        using var studentClient = MintedSessions.CreateStudentClient(_factory, student);

        // when/then: GET /labs под student → 403 'Доступ запрещён', тело — JSON-конверт.
        using var forbidden = await studentClient.GetAsync("/api/v1/labs");
        var forbiddenBody = await ApiAssert.AssertMessageAsync(
            forbidden, HttpStatusCode.Forbidden, "Доступ запрещён");
        Assert.Equal(JsonValueKind.Object, forbiddenBody.ValueKind);

        // when/then: GET /labs анонимно → 401 'Не авторизован', тело — JSON-конверт.
        using var anonymous = MintedSessions.Create(_factory);
        using var unauthorized = await anonymous.GetAsync("/api/v1/labs");
        var unauthorizedBody = await ApiAssert.AssertMessageAsync(
            unauthorized, HttpStatusCode.Unauthorized, "Не авторизован");
        Assert.Equal(JsonValueKind.Object, unauthorizedBody.ValueKind);
    }
}
