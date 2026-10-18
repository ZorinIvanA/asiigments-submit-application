using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-076 (P0, data_integrity; FR-014/FR-006) «reset-password: полевая ошибка не
/// гасит токен».
/// given: живой resetToken.
/// when:  POST с password 'abc' (confirmPassword 'abc'); затем повтор с валидным
///        password 'NewPass1!'.
/// then:  первый — 400 'Данные заполнены неверно' + errors.password (тексты
///        min/digit/special); токен остался живым — повтор с валидным паролем
///        даёт 204. FR-014 AC «Полевая ошибка не гасит токен».
/// </summary>
public sealed class Ts076_ResetPasswordFieldErrorKeepsTokenTests(B12RecoveryDevSpyHost factory)
    : IClassFixture<B12RecoveryDevSpyHost>
{
    private const string StudentEmail = "student01@example.com";
    private const string TokenValue = "ts076-live-reset-token";

    private readonly B12RecoveryDevSpyHost _factory = factory;

    [Fact]
    public async Task TS076_ResetPassword_WithFieldError_DoesNotConsumeTokenAndRetrySucceeds()
    {
        // given: живой resetToken (DI-сид через шов хранилища).
        var user = B12RecoveryStore.AddStudent(_factory, "student01", StudentEmail);
        var tokenHash = B12RecoveryStore.AddResetToken(_factory, user.Id, TokenValue);
        var securityTokens = _factory.Services.GetRequiredService<ISecurityTokenRepository>();
        using var client = B12RecoveryHttp.CreateClient(_factory);

        // when: POST с password 'abc' (confirmPassword 'abc').
        using var invalid = await B12RecoveryHarness.ResetPasswordAsync(client, TokenValue, "abc", "abc");

        // then: 400 'Данные заполнены неверно' + errors.password с текстами
        // min/digit/special (все сработавшие правила одновременно, FR-006).
        var body = await ApiAssert.AssertMessageAsync(
            invalid, HttpStatusCode.BadRequest, ErrorTexts.InvalidData);
        var passwordErrors = body.GetProperty("errors").GetProperty("password");
        Assert.Equal(JsonValueKind.Array, passwordErrors.ValueKind);
        var texts = passwordErrors.EnumerateArray()
            .Select(item => item.GetString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
        var expected = new[]
            {
                ErrorTexts.PasswordMin,
                ErrorTexts.PasswordDigit,
                ErrorTexts.PasswordSpecial,
            }
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, texts);

        // then: токен остался живым (полевая ошибка НЕ гасит токен).
        Assert.NotNull(securityTokens.FindLiveResetByHash(tokenHash));

        // then: повтор с валидным password даёт 204.
        using var retry = await B12RecoveryHarness.ResetPasswordAsync(client, TokenValue, "NewPass1!", "NewPass1!");
        await B12RecoveryHttp.AssertNoContentAsync(retry);
    }
}
