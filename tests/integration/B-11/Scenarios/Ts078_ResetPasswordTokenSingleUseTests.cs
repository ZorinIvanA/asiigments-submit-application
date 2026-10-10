using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// TS-078 (P1, negative; FR-014) «reset-password: одноразовость токена».
/// given: успешный сброс уже выполнен этим токеном.
/// when:  повторный POST с тем же resetToken и другим валидным паролем.
/// then:  400 'Ссылка восстановления недействительна или истекла' (использованный
///        токен неживой). FR-014.
/// </summary>
public sealed class Ts078_ResetPasswordTokenSingleUseTests(B11TimedWebAppFactory factory)
    : IClassFixture<B11TimedWebAppFactory>
{
    private const string StudentEmail = "student01@example.com";
    private const string TokenValue = "ts078-single-use-reset-token";

    private readonly B11TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS078_ResetPassword_SecondUseOfSameToken_IsRejected()
    {
        // given: успешный сброс уже выполнен этим токеном (живой токен DI-сидом,
        // первый POST даёт 204).
        var user = B11RecoverySeed.AddStudent(_factory, "student01", StudentEmail);
        B11RecoverySeed.AddResetToken(_factory, user.Id, TokenValue);
        using var client = HostClients.Create(_factory);

        using var first = await B11RecoveryApi.ResetPasswordAsync(client, TokenValue, "NewPass1!", "NewPass1!");
        await B11RecoveryApi.AssertNoContentAsync(first);

        // when: повторный POST с тем же resetToken и другим валидным паролем.
        using var second = await B11RecoveryApi.ResetPasswordAsync(client, TokenValue, "AnotherPass1!", "AnotherPass1!");

        // then: 400 'Ссылка восстановления недействительна или истекла' (использованный
        // токен неживой — одноразовость).
        await ApiAssert.AssertMessageAsync(second, HttpStatusCode.BadRequest, ErrorTexts.ResetTokenInvalid);
    }
}
