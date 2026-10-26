using System.Net;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Observability;
using LabsApp.Tests.Profile;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.Tests.Recovery;

// ============================================================================
// Защитная ветка IF-015 NOT_FOUND для POST /auth/reset-password (C-006, T-303):
// IUserRepository.SetPassword возвращает false, когда запись пользователя
// исчезла между проверкой живого токена (шаг 3 ResetPassword) и мутацией.
// Контракт ветки (IF-015 NOT_FOUND, doc-контракт IUserRepository.SetPassword):
// 401 «Не авторизован» — зеркало MePasswordController/ProfileController, БЕЗ
// гашения reset-токена, БЕЗ отзыва refresh-токенов, БЕЗ событий Api.Security
// (сброс не состоялся; IF-016 — запись только при фактическом выполнении).
//
// Ветвь недостижима HTTP-путём (операции удаления пользователя в API нет,
// ADR-043), поэтому окно детерминируется швом-декоратором
// <see cref="SetPasswordFailureRepository"/>: одноразовый Arm — следующий
// SetPassword отслеживаемого пользователя возвращает false, остальные операции
// проходят во внутреннее хранилище без изменений.
// ============================================================================

/// <summary>
/// Фикстура хоста с декоратором-швом SetPasswordFailureRepository (демо-сид
/// выключен, тестовые итерации KDF — Δkdf-гейты по снимкам IKdfCounter, FR-027).
/// Корневая фабрика открыта для log-sink (записи запросов к Host попадают в тот
/// же экземпляр провайдера — ConfigureWebHost базовой фабрики исполняется и для
/// производного хоста).
/// </summary>
public sealed class ResetPasswordProtectiveFixture : IDisposable
{
    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
            [AuthOptions.Pbkdf2IterationsVariable] = "1000",
        });

    /// <summary>Хост с подменённым IUserRepository (запросы и DI-сид — только он).</summary>
    public WebApplicationFactory<Program> Host { get; }

    /// <summary>Корневая фабрика: log-sink тестового хоста (NFR-006/IF-016).</summary>
    public TestWebAppFactory Root => _root;

    /// <summary>Шов отказa мутатора (один на хост, Arm на каждый сценарий).</summary>
    public SetPasswordFailureRepository Gate { get; }

    public ResetPasswordProtectiveFixture()
    {
        Host = _root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<SetPasswordFailureRepository>();
                services.AddSingleton<IUserRepository>(static sp =>
                    sp.GetRequiredService<SetPasswordFailureRepository>());
            });
        });
        Gate = Host.Services.GetRequiredService<SetPasswordFailureRepository>();
    }

    public void Dispose()
    {
        Host.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// Одноразовый шов NOT_FOUND: Arm фиксирует пользователя; следующий
/// SetPassword этого пользователя отвечает false (запись «исчезла»), не
/// доходя до внутреннего хранилища; остальные операции — прозрачный passthrough.
/// </summary>
public sealed class SetPasswordFailureRepository(InMemoryUserRepository inner) : IUserRepository
{
    private Guid _watchId;
    private bool _armed;

    /// <summary>Взводит шов: следующий SetPassword(watchId) вернёт false.</summary>
    public void Arm(Guid watchId)
    {
        _watchId = watchId;
        _armed = true;
    }

    /// <inheritdoc/>
    public async Task<bool> SetPasswordAsync(Guid userId, string passwordHash)
    {
        if (_armed && userId == _watchId)
        {
            _armed = false;
            return false;
        }

        return await inner.SetPasswordAsync(userId, passwordHash);
    }

    /// <inheritdoc/>
    public async Task AddAsync(User user) => await inner.AddAsync(user);

    /// <inheritdoc/>
    public async Task<User?> GetByIdAsync(Guid id) => await inner.GetByIdAsync(id);

    /// <inheritdoc/>
    public async Task<User?> GetByLoginAsync(string login) => await inner.GetByLoginAsync(login);

    /// <inheritdoc/>
    public async Task<User?> GetByEmailAsync(string email) => await inner.GetByEmailAsync(email);

    /// <inheritdoc/>
    public async Task UpdateAsync(User user) => await inner.UpdateAsync(user);

    /// <inheritdoc/>
    public async Task<UpdateProfileResult> UpdateProfileAsync(Guid userId, string fullName, string email) =>
        await inner.UpdateProfileAsync(userId, fullName, email);

    /// <inheritdoc/>
    public async Task<SetGroupResult> SetGroupAsync(Guid userId, Guid? groupId, Func<Guid, Task<bool>>? groupExists) =>
        await inner.SetGroupAsync(userId, groupId, groupExists);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<User>> ListStudentsAsync(string? search = null, string? groupIdFilter = null) =>
        await inner.ListStudentsAsync(search, groupIdFilter);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<User>> ListByGroupAsync(Guid groupId) => await inner.ListByGroupAsync(groupId);

    /// <inheritdoc/>
    public async Task<int> CountByGroupAsync(Guid groupId) => await inner.CountByGroupAsync(groupId);
}

/// <summary>
/// Защитная ветка SetPassword=false: 401 «Не авторизован» без errors; пароль не
/// применён; reset-токен и refresh-токен остались живы (без гашения и отзыва);
/// ни одной записи Api.Security; единственная деривация — Hash аргумента
/// мутатора (порядок Hash → SetPassword, Δkdf(reset_password)=1).
/// </summary>
public sealed class ResetPasswordProtectiveBranchTests(ResetPasswordProtectiveFixture fixture)
    : IClassFixture<ResetPasswordProtectiveFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;

    [Fact]
    public async Task Reset_SetPasswordNotFound_401Envelope_NoTokenConsumption_NoRevocation_NoEvents()
    {
        // given: пользователь с реальным хэшем; живые reset- и refresh-токены;
        // шов NOT_FOUND взведён; база Δkdf.
        var user = await Cr001GateHarness.SeedHashedUser(_factory, "reset-protective");
        var resetToken = await Cr001GateHarness.SeedResetToken(_factory, user.Id);
        var refreshToken = await Cr001GateHarness.SeedRefreshToken(_factory, user.Id);
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var before = Cr001GateHarness.KdfSnapshot(_factory);

        fixture.Gate.Arm(user.Id);
        using var client = Cr001GateHarness.CreateAnonymousClient(_factory);

        // when: сброс с живым токеном и валидной парой паролей — мутатор отвечает
        // NOT_FOUND (запись «исчезла» в окне).
        using var response = await RecoveryEndpointHarness.ResetAsync(
            client, resetToken, Cr001GateHarness.NewPassword, Cr001GateHarness.NewPassword);

        // then: 401 «Не авторизован» — конверт IF-001 без errors-карты.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            AuthCoreDefaults.UnauthorizedMessage,
            await RecoveryEndpointHarness.MessageAsync(response));
        using var body = await RecoveryEndpointHarness.ReadJsonAsync(response);
        Assert.False(body.RootElement.TryGetProperty("errors", out _));

        // then: единственная деривация — Hash аргумента мутатора (порядок
        // Hash → SetPassword); записи Api.Security нет — сброс не состоялся.
        Assert.Equal(
            1,
            Cr001GateHarness.KdfDeltaOfCaller(
                before,
                Cr001GateHarness.KdfSnapshot(_factory),
                KdfCallers.ResetPassword));
        Assert.Empty(SecurityEventLoggingHarness.SecurityRecords(fixture.Root));

        // then: без побочных эффектов — токен НЕ погашен, refresh НЕ отозван,
        // пароль в хранилище прежний.
        Assert.NotNull(await Cr001GateHarness.ResetTokenRecordAsync(_factory, resetToken));
        Assert.NotNull(await Cr001GateHarness.RefreshTokenRecordAsync(_factory, refreshToken));
        var stored = await Cr001GateHarness.StoredUserAsync(_factory, user.Id);
        Assert.True(hasher.Verify(
            Cr001GateHarness.TestUserPassword, stored.PasswordHash, KdfCallers.ResetPassword));
        Assert.False(hasher.Verify(
            Cr001GateHarness.NewPassword, stored.PasswordHash, KdfCallers.ResetPassword));
    }
}
