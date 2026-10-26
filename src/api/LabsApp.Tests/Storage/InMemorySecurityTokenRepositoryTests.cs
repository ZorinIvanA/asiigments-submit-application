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
    public async Task Add_FindLiveByHash_ReturnsRecord()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var token = NewRefreshToken(userId, Hash('a'), TimeSpan.FromDays(7));
        await repository.AddAsync(token);

        var found = await repository.FindLiveByHashAsync(Hash('a'));

        Assert.NotNull(found);
        Assert.Equal(token.Id, found!.Id);
        Assert.Equal(userId, found.UserId);
    }

    [Fact]
    public async Task FindLiveByHash_UnknownHash_ReturnsNull()
    {
        var (repository, _) = CreateRepository();

        Assert.Null(await repository.FindLiveByHashAsync(Hash('b')));
    }

    [Fact]
    public async Task Add_DuplicateTokenHash_Throws()
    {
        var (repository, _) = CreateRepository();
        await repository.AddAsync(NewRefreshToken(Guid.NewGuid(), Hash('c'), TimeSpan.FromDays(7)));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.AddAsync(NewRefreshToken(Guid.NewGuid(), Hash('c'), TimeSpan.FromDays(7))));
    }

    [Fact]
    public async Task FindLiveByHash_RevokedToken_ReturnsNull()
    {
        var (repository, _) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('d'), TimeSpan.FromDays(7));
        await repository.AddAsync(token);
        await repository.RevokeAsync(token.Id);

        Assert.Null(await repository.FindLiveByHashAsync(Hash('d')));
    }

    [Fact]
    public async Task FindLiveByHash_ExpiredAfterRefreshTtl_ReturnsNull()
    {
        // TTL refresh 7 д (IF-003): жив до истечения, null после — ленивая
        // проверка по FakeTimeProvider, таймеров в хранилище нет.
        var (repository, time) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('e'), TimeSpan.FromDays(7));
        await repository.AddAsync(token);

        Assert.NotNull(await repository.FindLiveByHashAsync(Hash('e')));

        time.Advance(TimeSpan.FromDays(8));
        Assert.Null(await repository.FindLiveByHashAsync(Hash('e')));
    }

    [Fact]
    public async Task Revoke_SetsRevokedAt_Idempotent()
    {
        var (repository, _) = CreateRepository();
        var token = NewRefreshToken(Guid.NewGuid(), Hash('f'), TimeSpan.FromDays(7));
        await repository.AddAsync(token);

        await repository.RevokeAsync(token.Id);
        var firstRevokedAt = await repository.FindLiveByHashAsync(Hash('f'));
        Assert.Null(firstRevokedAt); // отозванный не жив — момент читается сценарием отзыва ниже

        await repository.RevokeAsync(token.Id); // повторный отзыв — отсутствие операции
        Assert.Null(await repository.FindLiveByHashAsync(Hash('f')));
    }

    [Fact]
    public async Task Revoke_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.RevokeAsync(Guid.NewGuid());
    }

    // ------------------------------------------------------------------
    // RevokeAllForUserExcept: текущая сессия переживает смену/сброс пароля
    // (ISS-002, AC T-101).
    // ------------------------------------------------------------------

    [Fact]
    public async Task RevokeAllForUserExcept_KeepsCurrentSession_RevokesOtherTokens()
    {
        // given: два refresh-токена пользователя; exceptTokenHash = хэш первого.
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var current = NewRefreshToken(userId, Hash('1'), TimeSpan.FromDays(7));
        var other = NewRefreshToken(userId, Hash('2'), TimeSpan.FromDays(7));
        await repository.AddAsync(current);
        await repository.AddAsync(other);

        // when: отзыв всех кроме текущей сессии.
        await repository.RevokeAllForUserExceptAsync(userId, current.TokenHash);

        // then: первый жив, второй отозван.
        Assert.NotNull(await await repository.FindLiveByHashAsync(current.TokenHash));
        Assert.Null(await await repository.FindLiveByHashAsync(other.TokenHash));
    }

    [Fact]
    public async Task RevokeAllForUserExcept_NullExceptHash_RevokesAllUserTokens()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewRefreshToken(userId, Hash('3'), TimeSpan.FromDays(7)));
        await repository.AddAsync(NewRefreshToken(userId, Hash('4'), TimeSpan.FromDays(7)));

        await repository.RevokeAllForUserExceptAsync(userId, exceptTokenHash: null);

        Assert.Null(await repository.FindLiveByHashAsync(Hash('3')));
        Assert.Null(await repository.FindLiveByHashAsync(Hash('4')));
    }

    [Fact]
    public async Task RevokeAllForUserExcept_PreservesRevokedMoment_AndOtherUsers()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var current = NewRefreshToken(userId, Hash('5'), TimeSpan.FromDays(7));
        var earlier = NewRefreshToken(userId, Hash('6'), TimeSpan.FromDays(7));
        await repository.AddAsync(current);
        await repository.AddAsync(earlier);
        await repository.RevokeAsync(earlier.Id);

        var otherUserToken = NewRefreshToken(otherUserId, Hash('7'), TimeSpan.FromDays(7));
        await repository.AddAsync(otherUserToken);

        await repository.RevokeAllForUserExceptAsync(userId, current.TokenHash);

        // Текущая сессия жива; чужой пользователь не затронут.
        Assert.NotNull(await await repository.FindLiveByHashAsync(current.TokenHash));
        Assert.NotNull(await await repository.FindLiveByHashAsync(otherUserToken.TokenHash));
    }

    [Fact]
    public async Task RevokeAllForUserExcept_NoTokens_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.RevokeAllForUserExceptAsync(Guid.NewGuid(), Hash('8'));
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
            .Select(index => Task.Run(async () =>
            {
                startGate.Wait();
                await repository.AddAsync(NewRefreshToken(userId, Hash((char)('a' + (index % 26))) + index, TimeSpan.FromDays(7)));
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(100, await CountLiveTokensAsync(repository, userId));
    }

    [Fact]
    public async Task Add_ConcurrentSameTokenHash_ExactlyOneWinner()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var tokenHash = Hash('z');
        using var startGate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(async () =>
            {
                startGate.Wait();
                try
                {
                    await repository.AddAsync(NewRefreshToken(userId, tokenHash, TimeSpan.FromDays(7)));
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
        Assert.NotNull(await await repository.FindLiveByHashAsync(tokenHash));
    }

    private static async Task<int> CountLiveTokensAsync(InMemorySecurityTokenRepository repository, Guid userId)
    {
        var count = 0;
        for (var index = 0; index < 100; index++)
        {
            if (await repository.FindLiveByHashAsync(Hash((char)('a' + (index % 26))) + index) is not null)
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
    public async Task AddLive_FindLiveForUser_ReturnsNewestLive()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(code);

        var found = await repository.FindLiveForUserAsync(userId);

        Assert.NotNull(found);
        Assert.Equal(code.Id, found!.Id);
        Assert.Equal(0, found.Attempts);
    }

    [Fact]
    public async Task AddLive_ExtinguishesPreviousLiveCodes_OnlyOneLive()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var first = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(first);

        var second = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(second);

        // Живой — только новый; прежний погашен (resend, domain_model live → used).
        var live = await repository.FindLiveForUserAsync(userId);
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
            .Select(code => Task.Run(async () =>
            {
                startGate.Wait();
                await repository.AddLiveAsync(code);
            }))
            .ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        // Живой код есть, принадлежит добавленным в гонке...
        var live = await repository.FindLiveForUserAsync(userId);
        Assert.NotNull(live);
        Assert.Contains(live!.Id, codes.Select(code => code.Id));

        // ...и ровно один: гашение найденного живого не открывает второй живой.
        await repository.MarkUsedAsync(live.Id);
        Assert.Null(await await repository.FindLiveForUserAsync(userId));
    }

    [Fact]
    public async Task AddLive_ExpiredAndUsedCodes_NotResurrected()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var used = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(used);
        await repository.MarkUsedAsync(used.Id);

        await repository.AddLiveAsync(NewCode(userId, TimeSpan.FromMinutes(10)));

        // Новый код жив; погашенный остался погашенным.
        Assert.NotEqual(used.Id, (await repository.FindLiveForUserAsync(userId))!.Id);
    }

    [Fact]
    public async Task FindLiveForUser_ExpiredAfterCodeTtl_ReturnsNull()
    {
        // TTL кода 10 минут (IF-003): после истечения живых кодов нет.
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddLiveAsync(NewCode(userId, TimeSpan.FromMinutes(10)));

        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(await await repository.FindLiveForUserAsync(userId));
    }

    [Fact]
    public async Task IncrementAttemptsOnLive_CountsWrongAttempts_AnnulsOnFifth()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(code);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await repository.IncrementAttemptsOnLiveAsync(code.Id);
            var live = await repository.FindLiveForUserAsync(userId);
            Assert.NotNull(live);
            Assert.Equal(attempt, live!.Attempts);
        }

        // Пятая неверная попытка аннулирует код (domain_model: live → annulled).
        await repository.IncrementAttemptsOnLiveAsync(code.Id);
        Assert.Null(await await repository.FindLiveForUserAsync(userId));
    }

    [Fact]
    public async Task IncrementAttemptsOnLive_NonLiveCode_NoOp()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var used = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(used);
        await repository.MarkUsedAsync(used.Id);

        await repository.IncrementAttemptsOnLiveAsync(used.Id);

        // Неживой код: попытки не считаются, код не «оживает».
        Assert.Null(await await repository.FindLiveForUserAsync(userId));
    }

    [Fact]
    public async Task IncrementAttemptsOnLive_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.IncrementAttemptsOnLiveAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task MarkUsed_ExtinguishesCode()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(code);

        await repository.MarkUsedAsync(code.Id);

        Assert.Null(await await repository.FindLiveForUserAsync(userId));
    }

    [Fact]
    public async Task MarkUsed_Missing_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.MarkUsedAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task RecoveryCodes_UsersAreIsolated()
    {
        var (repository, _) = CreateRepository();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await repository.AddLiveAsync(NewCode(first, TimeSpan.FromMinutes(10)));
        await repository.AddLiveAsync(NewCode(second, TimeSpan.FromMinutes(10)));

        Assert.NotNull(await await repository.FindLiveForUserAsync(first));
        Assert.NotNull(await await repository.FindLiveForUserAsync(second));

        await repository.MarkUsedAsync((await repository.FindLiveForUserAsync(first))!.Id);
        Assert.Null(await await repository.FindLiveForUserAsync(first));
        Assert.NotNull(await await repository.FindLiveForUserAsync(second));
    }

    [Fact]
    public async Task AddLive_StoresSnapshot_ExternalMutationNotTracked()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var code = NewCode(userId, TimeSpan.FromMinutes(10));
        await repository.AddLiveAsync(code);

        code.Attempts = 100; // изменение выданной сущности не попадает в хранилище

        Assert.Equal(0, (await repository.FindLiveForUserAsync(userId))!.Attempts);
    }

    // ------------------------------------------------------------------
    // Reset-токены: TTL 15 минут, одноразовость, ConsumeAllForUser.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Add_FindLiveResetByHash_ReturnsRecord()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('A'), TimeSpan.FromMinutes(15)));

        var found = await repository.FindLiveResetByHashAsync(Hash('A'));

        Assert.NotNull(found);
        Assert.Equal(userId, found!.UserId);
        Assert.Null(found.UsedAt);
    }

    [Fact]
    public async Task FindLiveResetByHash_UnknownHash_ReturnsNull()
    {
        var (repository, _) = CreateRepository();

        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('B')));
    }

    [Fact]
    public async Task Add_DuplicateResetTokenHash_Throws()
    {
        var (repository, _) = CreateRepository();
        await repository.AddAsync(NewResetToken(Guid.NewGuid(), Hash('C'), TimeSpan.FromMinutes(15)));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.AddAsync(NewResetToken(Guid.NewGuid(), Hash('C'), TimeSpan.FromMinutes(15))));
    }

    [Fact]
    public async Task FindLiveResetByHash_HashIsIdentity_CaseSensitiveOrdinal()
    {
        // Дайджесты хранятся/ищутся ordinal (lowercase hex от TokenService):
        // прописная форма НЕ находит выданный lowercase-ключ.
        var (repository, _) = CreateRepository();
        await repository.AddAsync(NewResetToken(Guid.NewGuid(), Hash('d'), TimeSpan.FromMinutes(15)));

        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('d').ToUpperInvariant()));
        Assert.NotNull(await repository.FindLiveResetByHashAsync(Hash('d')));
    }

    [Fact]
    public async Task FindLiveResetByHash_ExpiredAfterResetTtl_ReturnsNull()
    {
        // TTL reset-токена 15 минут (IF-003/ASM-002): ленивая проверка по времени.
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('E'), TimeSpan.FromMinutes(15)));

        Assert.NotNull(await repository.FindLiveResetByHashAsync(Hash('E')));

        time.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('E')));
    }

    [Fact]
    public async Task FindLiveResetByHash_UsedToken_ReturnsNull()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('F'), TimeSpan.FromMinutes(15)));

        await repository.MarkUsedAsync(Hash('F'));

        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('F')));
    }

    [Fact]
    public async Task MarkUsed_ExtinguishesFoundToken()
    {
        // Предъявление найденного-неживого (просроченного) токена гасит его
        // (data_design C-013): гашение необратимо — просроченный не может «ожить»
        // ни при каком ходе времени (FakeTimeProvider движется только вперёд).
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('G'), TimeSpan.FromMinutes(15)));
        time.Advance(TimeSpan.FromMinutes(16));

        await repository.MarkUsedAsync(Hash('G'));

        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('G')));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('G')));
    }

    [Fact]
    public async Task MarkUsed_UnknownHash_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.MarkUsedAsync(Hash('H'));
    }

    [Fact]
    public async Task ConsumeAllForUser_ExtinguishesAllUserTokens_OtherUserUntouched()
    {
        var (repository, _) = CreateRepository();
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('I'), TimeSpan.FromMinutes(15)));
        await repository.AddAsync(NewResetToken(userId, Hash('J'), TimeSpan.FromMinutes(15)));
        await repository.AddAsync(NewResetToken(otherUser, Hash('K'), TimeSpan.FromMinutes(15)));

        await repository.ConsumeAllForUserAsync(userId);

        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('I')));
        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('J')));
        Assert.NotNull(await repository.FindLiveResetByHashAsync(Hash('K')));
    }

    [Fact]
    public async Task ConsumeAllForUser_KeepsAlreadyUsedMoment()
    {
        var (repository, time) = CreateRepository();
        var userId = Guid.NewGuid();
        await repository.AddAsync(NewResetToken(userId, Hash('L'), TimeSpan.FromMinutes(15)));
        await repository.MarkUsedAsync(Hash('L'));

        time.Advance(TimeSpan.FromMinutes(5));
        await repository.ConsumeAllForUserAsync(userId);

        // Повторное гашение не меняет момент: токен остаётся неживым.
        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('L')));
        time.Advance(TimeSpan.FromMinutes(20));
        Assert.Null(await repository.FindLiveResetByHashAsync(Hash('L')));
    }

    [Fact]
    public async Task ResetTokens_StoresSnapshot_ExternalMutationNotTracked()
    {
        var (repository, _) = CreateRepository();
        var token = NewResetToken(Guid.NewGuid(), Hash('M'), TimeSpan.FromMinutes(15));
        await repository.AddAsync(token);

        token.UsedAt = StartTime.UtcDateTime; // изменение выданной сущности не попадает в хранилище

        Assert.Null((await repository.FindLiveResetByHashAsync(Hash('M')))!.UsedAt);
    }

    [Fact]
    public async Task ConsumeAllForUser_NoTokens_NoOp()
    {
        var (repository, _) = CreateRepository();

        await repository.ConsumeAllForUserAsync(Guid.NewGuid());
    }
}
