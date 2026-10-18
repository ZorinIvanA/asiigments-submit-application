using System.Net;
using System.Net.Http.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using LabsApp.Tests.Profile;
using LabsApp.Tests.Students;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Эндпойнт-тест аменды CR-001 (T-204): POST /auth/reset-password применяет новый
// пароль УЗКОЙ атомарной мутацией IUserRepository.SetPassword (только
// PasswordHash) — конкурентные правка профиля и назначение группы студента,
// выполненные между чтением записи контроллером и мутацией, не откатываются.
// Регресс FR-014 при этом сохраняется: 204, Δkdf(reset_password)=1, ВСЕ
// reset-токены пользователя погашены.
//
// Окно гонки детерминировано швом <see cref="GatedUserRepository"/>: первый
// GetById пользователя — шаг (3) ResetPassword (после FindLiveResetByHash) —
// сигналит Arrived и удерживает контроллер до Release.
// ============================================================================

/// <summary>CR-001 для C-006 + регресс FR-014: сброс пароля не откатывает профиль и группу.</summary>
public sealed class ResetPasswordAtomicityTests(Cr001GateFixture fixture) : IClassFixture<Cr001GateFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;
    private readonly GatedUserRepository _gate = fixture.Gate;

    [Fact]
    public async Task Reset_ConcurrentProfileAndGroupEdits_NotReverted_AllResetTokensConsumed_DeltaKdfOne()
    {
        // given: пользователь; ДВА живых reset-токена (оба обязаны погаситься);
        // конкурентные цели — новое ФИО/email и группа ИК-222.
        var groupB = Cr001GateHarness.SeedGroup(_factory, "ИК-222");
        var user = Cr001GateHarness.SeedHashedUser(_factory, "reset-atomic");
        var resetToken = Cr001GateHarness.SeedResetToken(_factory, user.Id);
        var secondResetToken = Cr001GateHarness.SeedResetToken(_factory, user.Id);
        Assert.NotNull(Cr001GateHarness.ResetTokenRecord(_factory, resetToken));
        Assert.NotNull(Cr001GateHarness.ResetTokenRecord(_factory, secondResetToken));

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var users = Cr001GateHarness.Users(_factory);
        var before = Cr001GateHarness.KdfSnapshot(_factory);

        var (arrived, release) = _gate.Arm(user.Id);
        using var client = Cr001GateHarness.CreateAnonymousClient(_factory);

        // when: POST /auth/reset-password «в полёте»; в окне между чтением записи
        // пользователя (шаг 3) и мутацией — конкурентные UpdateProfile и SetGroup.
        var postTask = client.PostAsJsonAsync(
            Cr001GateHarness.ResetEndpoint,
            new
            {
                resetToken,
                password = Cr001GateHarness.NewPassword,
                confirmPassword = Cr001GateHarness.NewPassword,
            });

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(
            UpdateProfileResult.Success,
            users.UpdateProfile(user.Id, "Новое ФИО", "reset-atomic-new@example.com"));
        Assert.Equal(SetGroupResult.Success, users.SetGroup(user.Id, groupB.Id, _ => true));
        release.TrySetResult();

        using var response = await postTask;

        // then: 204 (арбитраж ISS-004); Δkdf(reset_password)=1 — ровно одна
        // деривация Hash нового пароля (регресс FR-014).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            1,
            Cr001GateHarness.KdfDeltaOfCaller(
                before,
                Cr001GateHarness.KdfSnapshot(_factory),
                KdfCallers.ResetPassword));

        // then: ВСЕ reset-токены пользователя погашены (регресс FR-014).
        Assert.Null(Cr001GateHarness.ResetTokenRecord(_factory, resetToken));
        Assert.Null(Cr001GateHarness.ResetTokenRecord(_factory, secondResetToken));

        // then: пароль сброшен (verify новым), конкурентные правка профиля и
        // группа НЕ откатаны узкой мутацией (суть CR-001 для C-006).
        var stored = Cr001GateHarness.StoredUser(_factory, user.Id);
        Assert.True(hasher.Verify(Cr001GateHarness.NewPassword, stored.PasswordHash, KdfCallers.ResetPassword));
        Assert.False(hasher.Verify(Cr001GateHarness.TestUserPassword, stored.PasswordHash, KdfCallers.ResetPassword));
        Assert.Equal("Новое ФИО", stored.FullName);
        Assert.Equal("reset-atomic-new@example.com", stored.Email);
        Assert.Equal(groupB.Id, stored.GroupId);
        Assert.Equal(user.Login, stored.Login);
    }
}

/// <summary>
/// AC T-303 «Конкурентность сброса и профиля»: конкурентный партнер сброса —
/// ПОЛНОЦЕННЫЙ HTTP-запрос PUT /me/profile (fullName/email), а не только
/// репозиторная мутация. POST /auth/reset-password задерживается швом
/// <see cref="GatedUserRepository"/> на GetById (шаг 3, после
/// FindLiveResetByHash); шов одноразовый, поэтому TryLoadCurrentUser
/// профильного запроса проходит свободно и UpdateProfile выполняется ЦЕЛИКОМ
/// в окне до мутации SetPassword. Инвариант CR-001: пароль применён (204), а
/// профильные поля равны применённым профилем — узкая мутация не откатывает их
/// устаревшим снимком; попутно — Δkdf(reset_password)=1, оба reset-токена
/// погашены, refresh-токен отозван.
/// </summary>
public sealed class ResetPasswordProfileEndpointAtomicityTests(Cr001GateFixture fixture)
    : IClassFixture<Cr001GateFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;
    private readonly GatedUserRepository _gate = fixture.Gate;

    [Fact]
    public async Task Reset_ConcurrentProfilePutOverHttp_ProfileFieldsKept_PasswordApplied()
    {
        // given: пользователь; ДВА живых reset-токена (оба погасятся успехом);
        // refresh-токен устройства (отзывается сбросом, FR-014).
        var user = Cr001GateHarness.SeedHashedUser(_factory, "reset-prof-atomic");
        var resetToken = Cr001GateHarness.SeedResetToken(_factory, user.Id);
        var secondResetToken = Cr001GateHarness.SeedResetToken(_factory, user.Id);
        var device = Cr001GateHarness.SeedRefreshToken(_factory, user.Id);
        Assert.NotNull(Cr001GateHarness.ResetTokenRecord(_factory, resetToken));
        Assert.NotNull(Cr001GateHarness.ResetTokenRecord(_factory, secondResetToken));

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = Cr001GateHarness.KdfSnapshot(_factory);

        var (arrived, release) = _gate.Arm(user.Id);
        using var client = Cr001GateHarness.CreateAnonymousClient(_factory);
        using var profileClient = Cr001GateHarness.CreateSessionClient(
            _factory, user.Id, UserRoles.Student);

        // when: POST /auth/reset-password «в полёте»; контроллер удерживается
        // швом на GetById; в окне — PUT /me/profile ЧЕРЕЗ HTTP (новые
        // fullName/email), ответ 200 до продолжения сброса.
        var postTask = client.PostAsJsonAsync(
            Cr001GateHarness.ResetEndpoint,
            new
            {
                resetToken,
                password = Cr001GateHarness.NewPassword,
                confirmPassword = Cr001GateHarness.NewPassword,
            });

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        const string profileFullName = "Новое ФИО";
        const string profileEmail = "reset-prof-atomic-new@example.com";
        using var putResponse = await profileClient.PutAsJsonAsync(
            Cr001GateHarness.ProfileEndpoint,
            new { fullName = profileFullName, email = profileEmail });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        release.TrySetResult();

        using var response = await postTask;

        // then: 204 (арбитраж ISS-004); Δkdf(reset_password)=1 — ровно одна
        // деривация Hash нового пароля (регресс FR-014).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            1,
            Cr001GateHarness.KdfDeltaOfCaller(
                before,
                Cr001GateHarness.KdfSnapshot(_factory),
                KdfCallers.ResetPassword));

        // then: пароль применён (verify новым), профильные поля — ОТ PUT /me/profile
        // (не откатаны сбросом), прочие поля записи не тронуты.
        var stored = Cr001GateHarness.StoredUser(_factory, user.Id);
        Assert.True(hasher.Verify(Cr001GateHarness.NewPassword, stored.PasswordHash, KdfCallers.ResetPassword));
        Assert.False(hasher.Verify(Cr001GateHarness.TestUserPassword, stored.PasswordHash, KdfCallers.ResetPassword));
        Assert.Equal(profileFullName, stored.FullName);
        Assert.Equal(profileEmail, stored.Email);
        Assert.Equal(user.Login, stored.Login);

        // then: ВСЕ reset-токены пользователя погашены; refresh-токен отозван.
        Assert.Null(Cr001GateHarness.ResetTokenRecord(_factory, resetToken));
        Assert.Null(Cr001GateHarness.ResetTokenRecord(_factory, secondResetToken));
        Assert.Null(Cr001GateHarness.RefreshTokenRecord(_factory, device));
    }
}
