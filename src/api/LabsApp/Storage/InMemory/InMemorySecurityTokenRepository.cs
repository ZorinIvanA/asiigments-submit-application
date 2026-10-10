using LabsApp.Domain.Entities;

namespace LabsApp.Storage.InMemory;

/// <summary>
/// In-memory реализация <see cref="ISecurityTokenRepository"/> (IF-015, FR-024):
/// консолидированное хранилище refresh-токенов, кодов восстановления и reset-токенов
/// (v2.2/ADR-008; консолидация задачи T-101). Dictionary + индексы
/// (byTokenHash Ordinal / byUserId), единая блокировка хранилища <see cref="StorageLock"/>
/// (ADR-002), метки времени — TimeProvider (FR-003). Хранятся только хэши; живость
/// вычисляется лениво (без таймеров); DI-singleton, состояние сбрасывается перезапуском.
/// Хранилище хранит и отдаёт КОПИИ-снимки сущностей.
/// </summary>
public sealed class InMemorySecurityTokenRepository(StorageLock lockObject, TimeProvider timeProvider) :
    ISecurityTokenRepository
{
    /// <summary>Число неверных попыток подтверждения кода, после которого код аннулируется (domain_model).</summary>
    private const int MaxRecoveryAttempts = 5;

    // Refresh-токены: Dictionary по Id + индекс по хэшу (точное сравнение Ordinal).
    private readonly Dictionary<Guid, RefreshToken> _refreshTokens = new();
    private readonly Dictionary<string, Guid> _refreshByTokenHash = new(StringComparer.Ordinal);

    // Коды восстановления: Dictionary по Id + индекс по владельцу (инвариант
    // «живой код — максимум один» удерживается атомарно в AddLive).
    private readonly Dictionary<Guid, RecoveryCode> _recoveryCodes = new();
    private readonly Dictionary<Guid, List<Guid>> _recoveryByUserId = new();

    // Reset-токены: Dictionary по дайджесту (идентификатор записи — сам TokenHash)
    // + индекс по владельцу для ConsumeAllForUser.
    private readonly Dictionary<string, PasswordResetToken> _resetTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<string>> _resetByUserId = new();

    // ------------------------------------------------------------------
    // Refresh-токены
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Add(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (lockObject.SyncRoot)
        {
            if (_refreshByTokenHash.ContainsKey(token.TokenHash))
            {
                throw new InvalidOperationException(
                    "Refresh-токен с таким хэшем уже зарегистрирован (ожидалась уникальная CSPRNG-серия).");
            }

            _refreshTokens[token.Id] = Clone(token);
            _refreshByTokenHash[token.TokenHash] = token.Id;
        }
    }

    /// <inheritdoc/>
    public RefreshToken? FindLiveByHash(string tokenHash)
    {
        lock (lockObject.SyncRoot)
        {
            if (!_refreshByTokenHash.TryGetValue(tokenHash, out var id))
            {
                return null;
            }

            var token = _refreshTokens[id];
            return token.RevokedAt is null && token.ExpiresAt > Now() ? Clone(token) : null;
        }
    }

    /// <inheritdoc/>
    public void Revoke(Guid id)
    {
        lock (lockObject.SyncRoot)
        {
            if (_refreshTokens.TryGetValue(id, out var token) && token.RevokedAt is null)
            {
                token.RevokedAt = Now();
            }
        }
    }

    /// <inheritdoc/>
    public void RevokeAllForUserExcept(Guid userId, string? exceptTokenHash)
    {
        lock (lockObject.SyncRoot)
        {
            foreach (var token in _refreshTokens.Values)
            {
                if (token.UserId == userId
                    && token.RevokedAt is null
                    && !string.Equals(token.TokenHash, exceptTokenHash, StringComparison.Ordinal))
                {
                    token.RevokedAt = Now();
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Коды восстановления
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void AddLive(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        lock (lockObject.SyncRoot)
        {
            // Resend: все незагашенные коды владельца гасятся в той же критической
            // секции, что и вставка нового кода (domain_model: live → used при resend).
            if (_recoveryByUserId.TryGetValue(code.UserId, out var userCodeIds))
            {
                var now = Now();
                foreach (var codeId in userCodeIds)
                {
                    var existing = _recoveryCodes[codeId];
                    if (existing.UsedAt is null)
                    {
                        existing.UsedAt = now;
                    }
                }
            }

            _recoveryCodes[code.Id] = Clone(code);
            AddRecoveryToIndex(code.Id, code.UserId);
        }
    }

    /// <inheritdoc/>
    public RecoveryCode? FindLiveForUser(Guid userId)
    {
        lock (lockObject.SyncRoot)
        {
            RecoveryCode? latest = null;

            if (_recoveryByUserId.TryGetValue(userId, out var codeIds))
            {
                var now = Now();
                foreach (var codeId in codeIds)
                {
                    var candidate = _recoveryCodes[codeId];
                    var isLive = candidate.UsedAt is null && candidate.ExpiresAt > now;
                    if (isLive && (latest is null || candidate.CreatedAt >= latest.CreatedAt))
                    {
                        latest = candidate;
                    }
                }
            }

            return latest is null ? null : Clone(latest);
        }
    }

    /// <inheritdoc/>
    public void IncrementAttemptsOnLive(Guid codeId)
    {
        lock (lockObject.SyncRoot)
        {
            if (!_recoveryCodes.TryGetValue(codeId, out var code)
                || code.UsedAt is not null
                || code.ExpiresAt <= Now())
            {
                // Отсутствующий или неживой код: попытки не считаются.
                return;
            }

            code.Attempts += 1;
            if (code.Attempts >= MaxRecoveryAttempts)
            {
                // Пятая неверная попытка аннулирует код (domain_model: live → annulled).
                code.UsedAt = Now();
            }
        }
    }

    /// <inheritdoc/>
    public void MarkUsed(Guid codeId)
    {
        lock (lockObject.SyncRoot)
        {
            if (_recoveryCodes.TryGetValue(codeId, out var code) && code.UsedAt is null)
            {
                code.UsedAt = Now();
            }
        }
    }

    // ------------------------------------------------------------------
    // Reset-токены
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public void Add(PasswordResetToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (lockObject.SyncRoot)
        {
            if (_resetTokens.ContainsKey(token.TokenHash))
            {
                throw new InvalidOperationException(
                    "Reset-токен уже зарегистрирован: коллизия SHA-256-дайджеста CSPRNG-токена невозможна.");
            }

            _resetTokens[token.TokenHash] = Clone(token);
            AddResetToIndex(token.TokenHash, token.UserId);
        }
    }

    /// <inheritdoc/>
    public PasswordResetToken? FindLiveResetByHash(string resetTokenHash)
    {
        lock (lockObject.SyncRoot)
        {
            if (!_resetTokens.TryGetValue(resetTokenHash, out var record))
            {
                return null;
            }

            return record.UsedAt is null && record.ExpiresAt > Now() ? Clone(record) : null;
        }
    }

    /// <inheritdoc/>
    public void MarkUsed(string resetTokenHash)
    {
        lock (lockObject.SyncRoot)
        {
            if (_resetTokens.TryGetValue(resetTokenHash, out var record) && record.UsedAt is null)
            {
                record.UsedAt = Now();
            }
        }
    }

    /// <inheritdoc/>
    public void ConsumeAllForUser(Guid userId)
    {
        lock (lockObject.SyncRoot)
        {
            if (!_resetByUserId.TryGetValue(userId, out var tokenHashes))
            {
                return;
            }

            var now = Now();
            foreach (var tokenHash in tokenHashes)
            {
                var record = _resetTokens[tokenHash];
                if (record.UsedAt is null)
                {
                    record.UsedAt = now;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Служебное
    // ------------------------------------------------------------------

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private void AddRecoveryToIndex(Guid codeId, Guid userId)
    {
        if (!_recoveryByUserId.TryGetValue(userId, out var codeIds))
        {
            codeIds = [];
            _recoveryByUserId[userId] = codeIds;
        }

        if (!codeIds.Contains(codeId))
        {
            codeIds.Add(codeId);
        }
    }

    private void AddResetToIndex(string tokenHash, Guid userId)
    {
        if (!_resetByUserId.TryGetValue(userId, out var tokenHashes))
        {
            tokenHashes = [];
            _resetByUserId[userId] = tokenHashes;
        }

        if (!tokenHashes.Contains(tokenHash))
        {
            tokenHashes.Add(tokenHash);
        }
    }

    private static RefreshToken Clone(RefreshToken token) => new()
    {
        Id = token.Id,
        UserId = token.UserId,
        TokenHash = token.TokenHash,
        ExpiresAt = token.ExpiresAt,
        RevokedAt = token.RevokedAt,
        CreatedAt = token.CreatedAt,
    };

    private static RecoveryCode Clone(RecoveryCode code) => new()
    {
        Id = code.Id,
        UserId = code.UserId,
        CodeHash = code.CodeHash,
        ExpiresAt = code.ExpiresAt,
        UsedAt = code.UsedAt,
        Attempts = code.Attempts,
        CreatedAt = code.CreatedAt,
    };

    private static PasswordResetToken Clone(PasswordResetToken token) => new()
    {
        TokenHash = token.TokenHash,
        UserId = token.UserId,
        ExpiresAt = token.ExpiresAt,
        UsedAt = token.UsedAt,
    };
}
