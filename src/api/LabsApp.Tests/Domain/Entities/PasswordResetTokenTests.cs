using LabsApp.Domain.Entities;

namespace LabsApp.Tests.Domain.Entities;

/// <summary>
/// Юнит-проверка формы сущности PasswordResetToken (v2.2, ADR-008/ASM-002, T-002):
/// идентификатор записи — SHA-256-дайджест непрозрачного токена (TokenHash), запись
/// создаётся при выдаче токена с expiresAt (TTL 15 минут), UsedAt = null до применения
/// (одноразовость); UserId нужен для отзыва ВСЕХ токенов пользователя при сбросе (FR-014).
/// </summary>
public sealed class PasswordResetTokenTests
{
    [Fact]
    public void FreshToken_HasNullUsedAt_Sha256HexShape()
    {
        var expiresAt = new DateTime(2026, 10, 8, 12, 15, 0, DateTimeKind.Utc);
        var token = new PasswordResetToken
        {
            TokenHash = new string('a', 64), // 64 hex-символа — SHA-256 в lowercase hex
            UserId = Guid.NewGuid(),
            ExpiresAt = expiresAt,
        };

        Assert.Null(token.UsedAt);
        Assert.Equal(expiresAt, token.ExpiresAt);
        Assert.Equal(64, token.TokenHash.Length);
    }

    [Fact]
    public void Consume_SetsUsedAt()
    {
        var usedAt = new DateTime(2026, 10, 8, 12, 10, 0, DateTimeKind.Utc);
        var token = new PasswordResetToken
        {
            TokenHash = new string('b', 64),
            UserId = Guid.NewGuid(),
            ExpiresAt = usedAt.AddMinutes(15),
        };

        token.UsedAt = usedAt;

        Assert.Equal(usedAt, token.UsedAt);
    }

    [Fact]
    public void TokenHash_IsIdentity_MutableForStoreKeying()
    {
        // Хранилище ключуется дайджестом (поиск при применении токена — FR-014):
        // два токена с разными дайджестами — разные записи.
        var first = new PasswordResetToken { TokenHash = new string('c', 64) };
        var second = new PasswordResetToken { TokenHash = new string('d', 64) };

        Assert.NotEqual(first.TokenHash, second.TokenHash);
        Assert.Equal(new string('c', 64), first.TokenHash);
    }
}
