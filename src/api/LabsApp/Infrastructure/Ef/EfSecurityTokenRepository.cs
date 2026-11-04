using LabsApp.Domain.Entities;
using LabsApp.Storage.Ef;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LabsApp.Storage;

/// <summary>
/// EF Core (PostgreSQL) реализация <see cref="ISecurityTokenRepository"/>:
/// refresh-токены, коды восстановления и reset-токены (консолидация T-101).
/// «Живость» вычисляется лениво по TimeProvider (ADR-002), таймеров нет;
/// гашение кодов/токенов — точечные UPDATE. Инвариант «живой код максимум
/// один» удерживается транзакцией AddLive.
/// </summary>
public sealed class EfSecurityTokenRepository(LabsAppDbContext db, TimeProvider timeProvider) :
    ISecurityTokenRepository
{
    /// <summary>Число неверных попыток, после которого код аннулируется (domain_model).</summary>
    private const int MaxRecoveryAttempts = 5;

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------
    // Refresh-токены
    // ------------------------------------------------------------------

    public async Task AddAsync(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        db.RefreshTokens.Add(token);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(token).State = EntityState.Detached;
            throw new InvalidOperationException(
                "Refresh-токен с таким хэшем уже зарегистрирован (ожидалась уникальная CSPRNG-серия).");
        }
    }

    public async Task<RefreshToken?> FindLiveByHashAsync(string tokenHash)
    {
        var now = Now();
        return await db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash
                && t.RevokedAt == null
                && t.ExpiresAt > now);
    }

    public async Task RevokeAsync(Guid id)
    {
        var now = Now();
        await db.RefreshTokens
            .Where(t => t.Id == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    public async Task RevokeAllForUserExceptAsync(Guid userId, string? exceptTokenHash)
    {
        var now = Now();
        var query = db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null);
        if (exceptTokenHash is not null)
        {
            query = query.Where(t => t.TokenHash != exceptTokenHash);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    // ------------------------------------------------------------------
    // Коды восстановления
    // ------------------------------------------------------------------

    public async Task AddLiveAsync(RecoveryCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Resend: прежние незагашенные коды владельца гасятся в одной транзакции
        // со вставкой нового (инвариант «живой — максимум один»).
        var now = Now();
        await db.RecoveryCodes
            .Where(c => c.UserId == code.UserId && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));

        db.RecoveryCodes.Add(code);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<RecoveryCode?> FindLiveForUserAsync(Guid userId)
    {
        var now = Now();
        return await db.RecoveryCodes.AsNoTracking()
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task IncrementAttemptsOnLiveAsync(Guid codeId)
    {
        var now = Now();
        var code = await db.RecoveryCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == codeId
                && c.UsedAt == null
                && c.ExpiresAt > now);
        if (code is null)
        {
            // Отсутствующий или неживой код: попытки не считаются.
            return;
        }

        var attempts = code.Attempts + 1;
        var annulled = attempts >= MaxRecoveryAttempts;

        // Пятая неверная попытка аннулирует код (live → annulled); аннулированный
        // код сохраняет момент гашения — потому UsedAt выставляется здесь же.
        if (annulled)
        {
            await db.RecoveryCodes
                .Where(c => c.Id == codeId && c.UsedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Attempts, attempts)
                    .SetProperty(c => c.UsedAt, now));
        }
        else
        {
            await db.RecoveryCodes
                .Where(c => c.Id == codeId && c.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, attempts));
        }
    }

    public async Task MarkUsedAsync(Guid codeId)
    {
        var now = Now();
        await db.RecoveryCodes
            .Where(c => c.Id == codeId && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));
    }

    // ------------------------------------------------------------------
    // Reset-токены
    // ------------------------------------------------------------------

    public async Task AddAsync(PasswordResetToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        db.PasswordResetTokens.Add(token);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(token).State = EntityState.Detached;
            throw new InvalidOperationException(
                "Reset-токен уже зарегистрирован: коллизия SHA-256-дайджеста CSPRNG-токена невозможна.");
        }
    }

    public async Task<PasswordResetToken?> FindLiveResetByHashAsync(string resetTokenHash)
    {
        var now = Now();
        return await db.PasswordResetTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == resetTokenHash
                && t.UsedAt == null
                && t.ExpiresAt > now);
    }

    public async Task MarkUsedAsync(string resetTokenHash)
    {
        var now = Now();
        await db.PasswordResetTokens
            .Where(t => t.TokenHash == resetTokenHash && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
    }

    public async Task ConsumeAllForUserAsync(Guid userId)
    {
        var now = Now();
        await db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
    }
}
