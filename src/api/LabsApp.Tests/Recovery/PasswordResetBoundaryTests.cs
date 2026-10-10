using System.Net;
using LabsApp.Auth;
using LabsApp.Domain.Validation;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ValidationTexts = LabsApp.Domain.Validation.ErrorTexts;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Граничные тесты POST /auth/reset-password (C-006, IF-008, FR-014; подзадача
// T-010): границы и lenient-ветки ввода, не покрытые RecoveryEndpointTests —
//  1) НИЖНЯЯ сторона верхней границы пароля: ровно 128 символов ДОПУСТИМ
//     (FR-006/ISS-016; rejected-сторона 129 — в RecoveryResetTests);
//  2) resetToken из одних пробелов — трим до пустой строки → ветка «не найден»
//     (RESET_LINK_INVALID, а не полевая валидация);
//  3) нестроковое значение поля resetToken (валидный JSON, число) → lenient-ридер
//     (ADR-014) даёт пустую строку → та же ветка «не найден».
// Во всех ветках отказа: Δkdf=0, чужие и собственные живые токены не гасятся,
// пароль пользователя не меняется. Харнес и фикстура переиспользованы из
// RecoveryEndpointTests (та же зона Recovery* — единственный писатель файлов).
// ============================================================================

/// <summary>
/// FR-014/FR-006 границы и lenient-ветки reset-password: граница 128 включительно
/// (успех с ровно 128 символами, Δkdf(reset_password)=1, вход по новому паролю);
/// пробельный и нестроковый resetToken — единая ветка RESET_LINK_INVALID без
/// гашения живых токенов (Δkdf=0).
/// </summary>
public sealed class PasswordResetBoundaryTests(RecoveryApiFixture fixture) : IClassFixture<RecoveryApiFixture>
{
    private readonly TestWebAppFactory _factory = fixture.Factory;

    [Fact]
    public async Task Reset_PasswordExactly128Chars_BoundaryAccepted_204_LoginWithNewPassword_DeltaKdfOne()
    {
        // given: живой reset-токен; база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-max128");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // given: пароль РОВНО 128 символов (граница включительно, ISS-016) с
        // корректным составом: буквы, цифра, спецзнак.
        var boundaryPassword = string.Concat("Aa1!", new string('x', 124));
        Assert.Equal(FieldValidators.PasswordMaxLength, boundaryPassword.Length);
        Assert.Empty(FieldValidators.Password(boundaryPassword));

        // when: сброс пароля граничным значением.
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, boundaryPassword, boundaryPassword);

        // then: 204 с пустым телом; Δkdf=1 ровно с меткой reset_password (FR-027).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await RecoveryEndpointHarness.AssertEmptyBodyAsync(response);
        var after = RecoveryEndpointHarness.KdfSnapshot(_factory);
        Assert.Equal(1, RecoveryEndpointHarness.KdfDelta(before, after));
        Assert.Equal(
            1,
            RecoveryEndpointHarness.KdfDeltaOfCaller(before, after, KdfCallers.ResetPassword));

        // then: граница не «срезается» хэшером — вход со старым паролем 401,
        // с новым (дословно 128 символов) — 200.
        using var oldLoginClient = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var oldLogin = await RecoveryEndpointHarness.LoginAsync(
            oldLoginClient, user.Login, RecoveryEndpointHarness.TestUserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        using var newLoginClient = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        using var newLogin = await RecoveryEndpointHarness.LoginAsync(
            newLoginClient, user.Login, boundaryPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Reset_WhitespaceResetToken_TrimsToEmpty_400ResetLinkInvalid_ZeroKdf_NothingExtinguished()
    {
        // given: пользователь с ЖИВЫМ reset-токеном (значение отлично от
        // предъявляемого); база Δkdf.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-blank");
        var liveToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);
        var before = RecoveryEndpointHarness.KdfSnapshot(_factory);

        // when: resetToken из одних пробелов — после трима пустая строка (IF-008).
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, "   ", RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);

        // then: 400 RESET_LINK_INVALID (ветка «не найден», НЕ полевая валидация);
        // Δkdf=0; живой токен пользователя не тронут; пароль прежний.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.ResetTokenInvalid, await RecoveryEndpointHarness.MessageAsync(response));
        Assert.Equal(
            0,
            RecoveryEndpointHarness.KdfDelta(before, RecoveryEndpointHarness.KdfSnapshot(_factory)));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, liveToken));
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetById(user.Id);
        Assert.NotNull(stored);
        Assert.True(_factory.Services.GetRequiredService<IPasswordHasher>()
            .Verify(RecoveryEndpointHarness.TestUserPassword, stored!.PasswordHash, KdfCallers.ResetPassword));
    }

    [Fact]
    public async Task Reset_NonStringResetToken_LenientEmpty_400ResetLinkInvalid_TokenKeptAlive()
    {
        // given: живой reset-токен с известным значением.
        var user = RecoveryEndpointHarness.SeedUser(_factory, "rec-reset-nonstring");
        var resetToken = RecoveryEndpointHarness.SeedResetToken(_factory, user.Id);
        using var client = RecoveryEndpointHarness.CreateAnonymousClient(_factory);

        // when: ВАЛИДНЫЙ JSON, но поле resetToken — число (нестроковое значение,
        // lenient-ридер ADR-014: Invalid → null → пустая строка после трима).
        using var response = await client.PostAsync(
            RecoveryEndpointHarness.ResetEndpoint,
            RecoveryEndpointHarness.Json("""{"resetToken": 42, "password": "NewPass1!", "confirmPassword": "NewPass1!"}"""));

        // then: 400 «Ссылка восстановления недействительна или истекла» (не 400
        // полевой валидации и не 500); токен остался живым (ветка «не найден»
        // гасит только НАЙДЕННЫЙ неживой, FR-014).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ValidationTexts.ResetTokenInvalid, await RecoveryEndpointHarness.MessageAsync(response));
        Assert.NotNull(RecoveryEndpointHarness.ResetTokenRecord(_factory, resetToken));

        // then: токен остался применимым — повтор с валидной парой паролей — 204.
        using var ok = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, RecoveryEndpointHarness.NewPassword, RecoveryEndpointHarness.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }
}
