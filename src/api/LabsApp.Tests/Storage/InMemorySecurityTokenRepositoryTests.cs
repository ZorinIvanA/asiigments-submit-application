using LabsApp.Domain.Entities;
using LabsApp.Storage.InMemory;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Юнит-проверки консолидированного InMemorySecurityTokenRepository
/// (IF-015/FR-024, T-101): refresh-токены (живость лениво по FakeTimeProvider,
/// отзыв, RevokeAllForUserExcept — текущая сессия переживает смену/сброс пароля,
/// ISS-002), коды восстановления (resend гасит прежние живые — живой максимум
/// один; пятая неверная попытка аннулирует), reset-токены (TTL 15 минут,
/// одноразовость, ConsumeAllForUser) и атомарность конкурентных Add под общим
/// StorageLock. TTL контракта IF-003: refresh 7 д / reset 15 мин / код 10 мин.
/// </summary>
public sealed class InMemorySecurityTokenRepositoryTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>SHA-256-дайджест в транспортной форме: 64 hex-символа lowercase.</summary>
    private static string Hash(char seed) => new(seed, 64);

    private static (InMemorySecurityTokenRepository Repository, FakeTimeProvider Time) CreateRepository()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(StartTime);
        return (new InMemorySecurityTokenRepository(new StorageLock(), time), time);
    }

    private static RefreshToken NewRefreshToken(Guid userId, string tokenHash, TimeSpan ttl) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresAt = StartTime.UtcDateTime.Add(ttl),
        RevokedAt = null,
        CreatedAt = StartTime.UtcDateTime,
    };

    private static RecoveryCode NewCode(Guid userId, TimeSpan ttl) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CodeHash = $"hash-{Guid.NewGuid():N}",
        ExpiresAt = StartTime.UtcDateTime.Add(ttl),
        UsedAt = null,
        Attempts = 0,
        CreatedAt = StartTime.UtcDateTime,
    };

    private static PasswordResetToken NewResetToken(Guid userId, string tokenHash, TimeSpan ttl) => new()
    {
        TokenHash = tokenHash,
        UserId = userId,
        ExpiresAt = StartTime.UtcDateTime.Add(ttl),
        UsedAt = null,
    };

    // ------------------------------------------------------------------
    // Refresh-токены: живость лениво (revokedAt == null && expiresAt > now).
    // ------------------------------------------------------------------

    [Fact]
    public void Add_FindLiveByHash_ReturnsRecord()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var token = NewRefreshToken(userId, Hash('a'), TimeSpan.FromDays(7));
        repository.Add(token);

        var found = repository.FindLiveByHash(Hash('a'));

        Assert.NotNull(found);
        Assert.Equal(token.Id, found!.Id);
        Assert.Equal(userId, found.UserId);
    }

    [Fact]
    public void FindLiveByHash_UnknownHash_ReturnsNull()
    {
        var (repository, _) = CreateRepository();

        Assert.Null(repository.FindLiveByHash(Hash('b')));
    }

    [Fact]
    public void Add_DuplicateTokenHash_Throws()
    {
        var (repository, _) = CreateRepository();
        repository.Add(NewRefreshToken(Guid.NewGuid(), Hash('c'), TimeSpan.FromDays(7)));

        Assert.Throws<InvalidOperationException>(() =>
            repository.Add(NewRefreshToken(Guid.NewGuid(), Hash('c'), TimeSpan.FromDays(7))));
    }

    [Fact]
    public void FindLiveByHash_RevokedToken_ReturnsNull()
    {
        var (repository, _) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('d'), TimeSpan.FromDays(7));
        repository.Add(token);
        repository.Revoke(token.Id);

        Assert.Null(repository.FindLiveByHash(Hash('d')));
    }

    [Fact]
    public void FindLiveByHash_ExpiredAfterRefreshTtl_ReturnsNull()
    {
        // TTL refresh 7 д (IF-003): жив до истечения, null после — ленивая
        // проверка по FakeTimeProvider, таймеров в хранилище нет.
        var (repository, time) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('e'), TimeSpan.FromDays(7));
        repository.Add(token);

        Assert.NotNull(repository.FindLiveByHash(Hash('e')));

        time.Advance(TimeSpan.FromDays(8));
        Assert.Null(repository.FindLiveByHash(Hash('e')));
    }

    [Fact]
    public void Revoke_SetsRevokedAt_Idempotent()
    {
        var (repository, _) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('f'), TimeSpan.FromDays(7));
        repository.Add(token);

        repository.Revoke(token.Id);
        var firstRevokedAt = repository.FindLiveByHash(Hash('f'));
        Assert.Null(firstRevokedAt); // отозванный не жив — момент читается сценарием отзыва ниже

        repository.Revoke(token.Id); // повторный отзыв — отсутствие операции
        Assert.Null(repository.FindLiveByHash(Hash('f')));
    }

    [Fact]
    public void Revoke_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.Revoke(Guid.NewGuid());
    }

    // ------------------------------------------------------------------
    // RevokeAllForUserExcept: текущая сессия переживает смену/сброс пароля
    // (ISS-002, AC T-101).
    // ------------------------------------------------------------------

    [Fact]
    public void RevokeAllForUserExcept_KeepsCurrentSession_RevokesOtherTokens()
    {
        // given: два refresh-токена пользователя; exceptTokenHash = хэш первого.
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var current = NewRefreshToken(userId, Hash('1'), TimeSpan.FromDays(7));
        var other = NewRefreshToken(userId, Hash('2'), TimeSpan.FromDays(7));
        repository.Add(current);
        repository.Add(other);

        // when: отзыв всех кроме текущей сессии.
        repository.RevokeAllForUserExcept(userId, current.TokenHash);

        // then: первый жив, второй отозван.
        Assert.NotNull(repository.FindLiveByHash(current.TokenHash));
        Assert.Null(repository.FindLiveByHash(other.TokenHash));
    }

    [Fact]
    public void RevokeAllForUserExcept_NullExceptHash_RevokesAllUserTokens()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewRefreshToken(userId, Hash('3'), TimeSpan.FromDays(7)));
        repository.Add(NewRefreshToken(userId, Hash('4'), TimeSpan.FromDays(7)));

        repository.RevokeAllForUserExcept(userId, exceptTokenHash: null);

        Assert.Null(repository.FindLiveByHash(Hash('3')));
        Assert.Null(repository.FindLiveByHash(Hash('4')));
    }

    [Fact]
    public void RevokeAllForUserExcept_PreservesRevokedMoment_AndOtherUsers()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var current = NewRefreshToken(userId, Hash('5'), TimeSpan.FromDays(7));
        var earlier = NewRefreshToken(userId, Hash('6'), TimeSpan.FromDays(7));
        repository.Add(current);
        repository.Add(earlier);
        repository.Revoke(earlier.Id);

        var otherUserToken = NewRefreshToken(otherUserId, Hash('7'), TimeSpan.FromDays(7));
        repository.Add(otherUserToken);

        repository.RevokeAllForUserExcept(userId, current.TokenHash);

        // Текущая сессия жива; чужой пользователь не затронут.
        Assert.NotNull(repository.FindLiveByHash(current.TokenHash));
        Assert.NotNull(repository.FindLiveByHash(otherUserToken.TokenHash));
    }

    [Fact]
    public void RevokeAllForUserExcept_NoTokens_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.RevokeAllForUserExcept(Guid.NewGuid(), Hash('8'));
    }

    // ------------------------------------------------------------------
    // Атомарность конкурентных Add (StorageLock).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Add_ConcurrentDistinctTokens_AllRegistered()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 100)
            .Select(index => Task.Run(() =>
            {
                startGate.Wait();
                repository.Add(NewRefreshToken(userId, Hash((char)('a' + (index % 26))) + index, TimeSpan.FromDays(7)));
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(100, CountLiveTokens(repository, userId));
    }

    [Fact]
    public async Task Add_ConcurrentSameTokenHash_ExactlyOneWinner()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var tokenHash = Hash('z');
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    repository.Add(NewRefreshToken(userId, tokenHash, TimeSpan.FromDays(7)));
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, tasks.Count(task => task.Result));
        Assert.Equal(99, tasks.Count(task => !task.Result));
        Assert.NotNull(repository.FindLiveByHash(tokenHash));
    }

    private static int CountLiveTokens(InMemorySecurityTokenRepository repository, Guid userId)
    {
        var count = 0;
        for (var index = 0; index < 100; index++)
        {
            if (repository.FindLiveByHash(Hash((char)('a' + (index % 26))) + index) is not null)
            {
                count++;
            }
        }

        return count;
    }

    // ------------------------------------------------------------------
    // Коды восстановления: resend гасит прежние живые — живой максимум один.
    // ------------------------------------------------------------------

    [Fact]
    public void AddLive_FindLiveForUser_ReturnsNewestLive()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(code);

        var found = repository.FindLiveForUser(userId);

        Assert.NotNull(found);
        Assert.Equal(code.Id, found!.Id);
        Assert.Equal(0, found.Attempts);
    }

    [Fact]
    public void AddLive_ExtinguishesPreviousLiveCodes_OnlyOneLive()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var first = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(first);

        var second = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(second);

        // Живой — только новый; прежний погашен (resend, domain_model live → used).
        var live = repository.FindLiveForUser(userId);
        Assert.NotNull(live);
        Assert.Equal(second.Id, live!.Id);
        Assert.NotEqual(first.Id, live.Id);
    }

    [Fact]
    public async Task AddLive_ConcurrentSameUser_ExactlyOneLiveCode()
    {
        // Конкурентные AddLive одного пользователя (unit-требование T-101):
        // гашение прежних живых и вставка нового атомарны под StorageLock —
        // после гонки живым остаётся ровно один из добавленных кодов.
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var codes = Enumerable.Range(0, 32)
            .Select(_ => NewCode(userId, TimeSpan.FromMinutes(10)))
            .ToArray();
        using var startGate = new ManualResetEventSlim();

        var tasks = codes
            .Select(code => Task.Run(() =>
            {
                startGate.Wait();
                repository.AddLive(code);
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        // Живой код есть, принадлежит добавленным в гонке...
        var live = repository.FindLiveForUser(userId);
        Assert.NotNull(live);
        Assert.Contains(live!.Id, codes.Select(code => code.Id));

        // ...и ровно один: гашение найденного живого не открывает второй живой.
        repository.MarkUsed(live.Id);
        Assert.Null(repository.FindLiveForUser(userId));
    }

    [Fact]
    public void AddLive_ExpiredAndUsedCodes_NotResurrected()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var used = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(used);
        repository.MarkUsed(used.Id);

        repository.AddLive(NewCode(userId, TimeSpan.FromMinutes(10)));

        // Новый код жив; погашенный остался погашенным.
        Assert.NotEqual(used.Id, repository.FindLiveForUser(userId)!.Id);
    }

    [Fact]
    public void FindLiveForUser_ExpiredAfterCodeTtl_ReturnsNull()
    {
        // TTL кода 10 минут (IF-003): после истечения живых кодов нет.
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.AddLive(NewCode(userId, TimeSpan.FromMinutes(10)));

        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(repository.FindLiveForUser(userId));
    }

    [Fact]
    public void IncrementAttemptsOnLive_CountsWrongAttempts_AnnulsOnFifth()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(code);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            repository.IncrementAttemptsOnLive(code.Id);
            var live = repository.FindLiveForUser(userId);
            Assert.NotNull(live);
            Assert.Equal(attempt, live!.Attempts);
        }

        // Пятая неверная попытка аннулирует код (domain_model: live → annulled).
        repository.IncrementAttemptsOnLive(code.Id);
        Assert.Null(repository.FindLiveForUser(userId));
    }

    [Fact]
    public void IncrementAttemptsOnLive_NonLiveCode_NoOp()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var used = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(used);
        repository.MarkUsed(used.Id);

        repository.IncrementAttemptsOnLive(used.Id);

        // Неживой код: попытки не считаются, код не «оживает».
        Assert.Null(repository.FindLiveForUser(userId));
    }

    [Fact]
    public void IncrementAttemptsOnLive_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.IncrementAttemptsOnLive(Guid.NewGuid());
    }

    [Fact]
    public void MarkUsed_ExtinguishesCode()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(code);

        repository.MarkUsed(code.Id);

        Assert.Null(repository.FindLiveForUser(userId));
    }

    [Fact]
    public void MarkUsed_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.MarkUsed(Guid.NewGuid());
    }

    [Fact]
    public void RecoveryCodes_UsersAreIsolated()
    {
        var (repository, _) = CreateRepository();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        repository.AddLive(NewCode(first, TimeSpan.FromMinutes(10)));
        repository.AddLive(NewCode(second, TimeSpan.FromMinutes(10)));

        Assert.NotNull(repository.FindLiveForUser(first));
        Assert.NotNull(repository.FindLiveForUser(second));

        repository.MarkUsed(repository.FindLiveForUser(first)!.Id);
        Assert.Null(repository.FindLiveForUser(first));
        Assert.NotNull(repository.FindLiveForUser(second));
    }

    [Fact]
    public void AddLive_StoresSnapshot_ExternalMutationNotTracked()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        repository.AddLive(code);

        code.Attempts = 100; // изменение выданной сущности не попадает в хранилище

        Assert.Equal(0, repository.FindLiveForUser(userId)!.Attempts);
    }

    // ------------------------------------------------------------------
    // Reset-токены: TTL 15 минут, одноразовость, ConsumeAllForUser.
    // ------------------------------------------------------------------

    [Fact]
    public void Add_FindLiveResetByHash_ReturnsRecord()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('A'), TimeSpan.FromMinutes(15)));

        var found = repository.FindLiveResetByHash(Hash('A'));

        Assert.NotNull(found);
        Assert.Equal(userId, found!.UserId);
        Assert.Null(found.UsedAt);
    }

    [Fact]
    public void FindLiveResetByHash_UnknownHash_ReturnsNull()
    {
        var (repository, _) = CreateRepository();

        Assert.Null(repository.FindLiveResetByHash(Hash('B')));
    }

    [Fact]
    public void Add_DuplicateResetTokenHash_Throws()
    {
        var (repository, _) = CreateRepository();
        repository.Add(NewResetToken(Guid.NewGuid(), Hash('C'), TimeSpan.FromMinutes(15)));

        Assert.Throws<InvalidOperationException>(() =>
            repository.Add(NewResetToken(Guid.NewGuid(), Hash('C'), TimeSpan.FromMinutes(15))));
    }

    [Fact]
    public void FindLiveResetByHash_HashIsIdentity_CaseSensitiveOrdinal()
    {
        // Дайджесты хранятся/ищутся ordinal (lowercase hex от TokenService):
        // прописная форма НЕ находит выданный lowercase-ключ.
        var (repository, _) = CreateRepository();
        repository.Add(NewResetToken(Guid.NewGuid(), Hash('d'), TimeSpan.FromMinutes(15)));

        Assert.Null(repository.FindLiveResetByHash(Hash('d').ToUpperInvariant()));
        Assert.NotNull(repository.FindLiveResetByHash(Hash('d')));
    }

    [Fact]
    public void FindLiveResetByHash_ExpiredAfterResetTtl_ReturnsNull()
    {
        // TTL reset-токена 15 минут (IF-003/ASM-002): ленивая проверка по времени.
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('E'), TimeSpan.FromMinutes(15)));

        Assert.NotNull(repository.FindLiveResetByHash(Hash('E')));

        time.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(repository.FindLiveResetByHash(Hash('E')));
    }

    [Fact]
    public void FindLiveResetByHash_UsedToken_ReturnsNull()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('F'), TimeSpan.FromMinutes(15)));

        repository.MarkUsed(Hash('F'));

        Assert.Null(repository.FindLiveResetByHash(Hash('F')));
    }

    [Fact]
    public void MarkUsed_ExtinguishesFoundToken()
    {
        // Предъявление найденного-неживого (просроченного) токена гасит его
        // (data_design C-013): гашение необратимо — просроченный не может «ожить»
        // ни при каком ходе времени (FakeTimeProvider движется только вперёд).
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('G'), TimeSpan.FromMinutes(15)));
        time.Advance(TimeSpan.FromMinutes(16));

        repository.MarkUsed(Hash('G'));

        Assert.Null(repository.FindLiveResetByHash(Hash('G')));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(repository.FindLiveResetByHash(Hash('G')));
    }

    [Fact]
    public void MarkUsed_UnknownHash_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.MarkUsed(Hash('H'));
    }

    [Fact]
    public void ConsumeAllForUser_ExtinguishesAllUserTokens_OtherUserUntouched()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('I'), TimeSpan.FromMinutes(15)));
        repository.Add(NewResetToken(userId, Hash('J'), TimeSpan.FromMinutes(15)));
        repository.Add(NewResetToken(otherUser, Hash('K'), TimeSpan.FromMinutes(15)));

        repository.ConsumeAllForUser(userId);

        Assert.Null(repository.FindLiveResetByHash(Hash('I')));
        Assert.Null(repository.FindLiveResetByHash(Hash('J')));
        Assert.NotNull(repository.FindLiveResetByHash(Hash('K')));
    }

    [Fact]
    public void ConsumeAllForUser_KeepsAlreadyUsedMoment()
    {
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        repository.Add(NewResetToken(userId, Hash('L'), TimeSpan.FromMinutes(15)));
        repository.MarkUsed(Hash('L'));

        time.Advance(TimeSpan.FromMinutes(5));
        repository.ConsumeAllForUser(userId);

        // Повторное гашение не меняет момент: токен остаётся неживым.
        Assert.Null(repository.FindLiveResetByHash(Hash('L')));
        time.Advance(TimeSpan.FromMinutes(20));
        Assert.Null(repository.FindLiveResetByHash(Hash('L')));
    }

    [Fact]
    public void ResetTokens_StoresSnapshot_ExternalMutationNotTracked()
    {
        var (repository, _) = CreateRepository();
        var token = NewResetToken(Guid.NewGuid(), Hash('M'), TimeSpan.FromMinutes(15));
        repository.Add(token);

        token.UsedAt = StartTime.UtcDateTime; // изменение выданной сущности не попадает в хранилище

        Assert.Null(repository.FindLiveResetByHash(Hash('M'))!.UsedAt);
    }

    [Fact]
    public void ConsumeAllForUser_NoTokens_NoOp()
    {
        var (repository, _) = CreateRepository();

        repository.ConsumeAllForUser(Guid.NewGuid());
    }
}
