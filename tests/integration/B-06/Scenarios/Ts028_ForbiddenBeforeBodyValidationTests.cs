using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-028 «СПО: 403 раньше 400 при невалидном теле» (негативный, P0,
/// FR-011 AC «Приоритет 403 над 400»: «СПО: роль раньше валидации»).
///
/// given: сессия student.
/// when:  POST /api/v1/labs {number:0, semester:1, content:'x', defenseRequired:false}
///        (number=0 нарушает полевую валидацию).
/// then:  HTTP 403 (не 400): проверка роли в едином порядке обработки /api
///        выполняется раньше валидации тела.
/// </summary>
public sealed class Ts028_ForbiddenBeforeBodyValidationTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts028_ForbiddenBeforeBodyValidationTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PostLabsWithInvalidBodyUnderStudentSession_Returns403Not400()
    {
        // given: сессия student.
        using var client = TestSessions.CreateStudentSession(
            _factory,
            login: "ts028.student",
            email: "ts028@example.com").Client;

        // when: POST /api/v1/labs с невалидным телом (number=0).
        using var response = await ApiRequests.CreateLabWithInvalidNumberAsync(client);

        // then: HTTP 403 (не 400) с телом {"message":"Доступ запрещён"}.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Доступ запрещён");
    }
}
