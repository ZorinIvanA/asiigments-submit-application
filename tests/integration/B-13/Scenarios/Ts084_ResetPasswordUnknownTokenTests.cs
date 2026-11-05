using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-084 (P0, negative; FR-014) «Reset-password: несуществующий токен — 400,
/// пароль не изменён».
/// given: в хранилище нет записи с SHA-256 предъявляемого значения (свежий хост;
///        пароль пользователя известен тесту).
/// when:  POST {resetToken:'garbage', password:'NewPass1!', confirmPassword:
///        'NewPass1!'}.
/// then:  400 'Ссылка восстановления недействительна или истекла'; пароль
///        пользователя не изменён (вход старым паролем — 200)
///        (FR-014 AC «Несуществующий токен»).
/// </summary>
public sealed class Ts084_ResetPasswordUnknownTokenTests(B13RecoveryTimedHost factory)
    : IClassFixture<B13RecoveryTimedHost>
{
    private const string StudentLogin = "student01";
    private const string StudentEmail = "student01@example.com";

    private readonly B13RecoveryTimedHost _factory = factory;

    [Fact]
    public async Task TS084_ResetPassword_WithUnknownToken_IsRejectedAndPasswordUnchanged()
    {
        // given: студент с известным старым паролем; в хранилище нет reset-токенов.
        B13RecoverySeed.AddStudent(
            _factory, StudentLogin, StudentEmail, B13RecoveryHarness.TestUserPassword);
        using var client = B13RecoveryHarness.CreateClient(_factory);

        // when: POST {resetToken:'garbage', ...валидные пароли...}.
        using var reset = await B13RecoveryApi.ResetPasswordAsync(
            client, "garbage", "NewPass1!", "NewPass1!");

        // then: 400 'Ссылка восстановления недействительна или истекла'.
        await ApiAssert.AssertMessageAsync(
            reset, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);

        // then: пароль пользователя не изменён — вход старым паролем даёт 200.
        using var oldLogin = await B13RecoveryHarness.LoginAsync(
            client, StudentLogin, B13RecoveryHarness.TestUserPassword);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
    }
}
