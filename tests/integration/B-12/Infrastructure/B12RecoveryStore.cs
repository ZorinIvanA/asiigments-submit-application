using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// DI-сид и швы инспекции хранилища recovery/reset-кейсов батча B-12 (TS-064..TS-078;
/// копия механики зоны B-11 — чужие харнесы недоступны для ссылок, BL-001 BUG-001):
/// наполнение и инспекция ПРЯМО через публичные интерфейсы IF-015
/// (ISecurityTokenRepository: AddLive/Add/FindLiveForUser/FindLiveResetByHash;
/// ITokenService.HashRecoveryCode — тот же быстрый солёный SHA-256, что в
/// прод-потоке, ASM-005; пароль DI-учётки — IPasswordHasher.Hash с меткой seed,
/// формат IF-002, вход по нему работает). Код реализации не затрагивается;
/// обращение к factory.Services материализует хост (сид выполняется при построении
/// приложения до первого запроса).
///
/// Неживые записи (погашенный код, погашенный reset-токен) интерфейсом недоступны —
/// инспекция usedAt/attempts (TS-064/067/069/071/075) читает приватные словари
/// зарегистрированного InMemorySecurityTokenRepository рефлексией ПО ИМЕНИ поля.
/// Изменение внутреннего устройства хранилища даст внятный диагноз, а не исключение
/// отражения.
/// </summary>
public static class B12RecoveryStore
{
    /// <summary>
    /// Добавляет студента напрямую в IUserRepository (DI-сид кейса). Пароль DI-учётки
    /// хэшируется прод-хэшером (формат IF-002) — вход по нему работает (TS-073/TS-074);
    /// без пароля — маркер-only хэш.
    /// </summary>
    public static User AddStudent(
        WebApplicationFactory<Program> factory,
        string login,
        string email,
        string? password = null)
    {
        var services = factory.Services;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = password is null
                ? "di-seed-no-login"
                : services.GetRequiredService<IPasswordHasher>().Hash(password, KdfCallers.Seed),
            FullName = "Восстановление Тестович",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = now,
        };
        services.GetRequiredService<IUserRepository>().Add(user);
        return user;
    }

    /// <summary>
    /// Сеет живой код восстановления пользователя (ISecurityTokenRepository.AddLive):
    /// хэш — прод-сервис токенов (VerifyRecoveryCode подтверждает значение),
    /// TTL по умолчанию 10 минут (FR-012), attempts по умолчанию 0.
    /// </summary>
    public static RecoveryCode AddRecoveryCode(
        WebApplicationFactory<Program> factory,
        Guid userId,
        string code,
        int attempts = 0,
        TimeSpan? ttl = null)
    {
        var services = factory.Services;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var record = new RecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = services.GetRequiredService<ITokenService>().HashRecoveryCode(code),
            ExpiresAt = now.Add(ttl ?? TimeSpan.FromMinutes(10)),
            Attempts = attempts,
            UsedAt = null,
            CreatedAt = now,
        };
        services.GetRequiredService<ISecurityTokenRepository>().AddLive(record);
        return record;
    }

    /// <summary>
    /// Сеет reset-токен с известным тесту значением (ISecurityTokenRepository.Add):
    /// в хранилище — только SHA-256-дайджест значения (нижний регистр hex);
    /// TTL по умолчанию 15 минут (IF-003). Возвращает дайджест записи.
    /// </summary>
    public static string AddResetToken(
        WebApplicationFactory<Program> factory,
        Guid userId,
        string value,
        TimeSpan? ttl = null)
    {
        var services = factory.Services;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var hash = Sha256Hex(value);
        services.GetRequiredService<ISecurityTokenRepository>().Add(new PasswordResetToken
        {
            TokenHash = hash,
            UserId = userId,
            ExpiresAt = now.Add(ttl ?? TimeSpan.FromMinutes(15)),
            UsedAt = null,
        });
        return hash;
    }

    /// <summary>SHA-256 hex (строчные) значения — зеркало хранилища reset-токенов (IF-003).</summary>
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ------------------------------------------------------------------
    // Шов инспекции хранилища (неживые записи через интерфейс недоступны)
    // ------------------------------------------------------------------

    /// <summary>КОПИИ всех записей кодов восстановления (включая неживые).</summary>
    public static IReadOnlyList<RecoveryCode> AllRecoveryCodes(WebApplicationFactory<Program> factory)
    {
        var source = RequireField<Dictionary<Guid, RecoveryCode>>(factory, "_recoveryCodes");
        lock (((ICollection)source).SyncRoot)
        {
            return source.Values.Select(Copy).ToArray();
        }
    }

    /// <summary>Копия записи кода по идентификатору (включая неживые) либо null.</summary>
    public static RecoveryCode? RecoveryCodeById(WebApplicationFactory<Program> factory, Guid codeId)
    {
        var source = RequireField<Dictionary<Guid, RecoveryCode>>(factory, "_recoveryCodes");
        lock (((ICollection)source).SyncRoot)
        {
            return source.TryGetValue(codeId, out var record) ? Copy(record) : null;
        }
    }

    /// <summary>Копия записи reset-токена по дайджесту (включая неживые) либо null.</summary>
    public static PasswordResetToken? ResetTokenByHash(WebApplicationFactory<Program> factory, string tokenHash)
    {
        var source = RequireField<Dictionary<string, PasswordResetToken>>(factory, "_resetTokens");
        lock (((ICollection)source).SyncRoot)
        {
            return source.TryGetValue(tokenHash, out var record) ? Copy(record) : null;
        }
    }

    private static T RequireField<T>(WebApplicationFactory<Program> factory, string fieldName)
        where T : class
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        var field = repository.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(
            field is not null,
            $"Шов инспекции хранилища сломан: у {repository.GetType().Name} нет поля «{fieldName}» " +
            "(внутреннее устройство хранилища изменилось — обновите шов зоны B-12).");
        var value = field!.GetValue(repository) as T;
        Assert.True(
            value is not null,
            $"Поле «{fieldName}» хранилища {repository.GetType().Name} не является {typeof(T).Name}.");
        return value!;
    }

    private static RecoveryCode Copy(RecoveryCode code) => new()
    {
        Id = code.Id,
        UserId = code.UserId,
        CodeHash = code.CodeHash,
        ExpiresAt = code.ExpiresAt,
        UsedAt = code.UsedAt,
        Attempts = code.Attempts,
        CreatedAt = code.CreatedAt,
    };

    private static PasswordResetToken Copy(PasswordResetToken token) => new()
    {
        TokenHash = token.TokenHash,
        UserId = token.UserId,
        ExpiresAt = token.ExpiresAt,
        UsedAt = token.UsedAt,
    };
}
