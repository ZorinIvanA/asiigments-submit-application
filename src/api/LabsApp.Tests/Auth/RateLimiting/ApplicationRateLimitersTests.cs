using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth.RateLimiting;

/// <summary>
/// Юнит-проверки прикладных лимитеров (IF-006, матрица FR-004): login-failure
/// 5/60с на «lower(trim(login))|IP» (ShouldBlock/RegisterFailure — путь отказа),
/// register 5/3600с на IP (TryAcquire все попытки), recovery_request 3/3600с на
/// lower(trim(email)) (TryAcquire до проверки email); нормализация трёх схем
/// ключей (trim+lower+усечение, нестрока → ''); константы матрицы; сценарий
/// потолка ключей и overflow-корзины на прикладном лимитере без единого KDF.
/// </summary>
public sealed class ApplicationRateLimitersTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------
    // Матрица FR-004: окна и лимиты трёх политик.
    // ------------------------------------------------------------------

    [Fact]
    public void Matrix_FixedWindowsAndLimits()
    {
        Assert.Equal(60_000, LoginFailureLimiter.FailureWindowMs);
        Assert.Equal(5, LoginFailureLimiter.FailureLimit);
        Assert.Equal(3_600_000, RegisterLimiter.RegisterWindowMs);
        Assert.Equal(5, RegisterLimiter.RegisterLimit);
        Assert.Equal(3_600_000, RecoveryRequestLimiter.RequestWindowMs);
        Assert.Equal(3, RecoveryRequestLimiter.RequestLimit);
    }

    // ------------------------------------------------------------------
    // RegisterLimiter: 5/3600с на IP, ВСЕ попытки.
    // ------------------------------------------------------------------

    [Fact]
    public void Register_FirstFiveAllowed_SixthRejected_PerIp()
    {
        var limiter = new RegisterLimiter(new FakeTimeProvider());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(limiter.TryAcquire("10.0.0.1"), $"попытка {attempt}");
        }

        Assert.False(limiter.TryAcquire("10.0.0.1"));
        Assert.True(limiter.TryAcquire("10.0.0.2"));
    }

    [Fact]
    public void Register_RetryAfterSeconds_CountsDownUntilWindowFrees()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(StartTime);
        var limiter = new RegisterLimiter(time);

        Assert.Null(limiter.RetryAfterSeconds("10.0.0.1"));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(limiter.TryAcquire("10.0.0.1"));
        }

        Assert.Equal(3600, limiter.RetryAfterSeconds("10.0.0.1"));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1800, limiter.RetryAfterSeconds("10.0.0.1"));
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(limiter.TryAcquire("10.0.0.1"));
        Assert.Null(limiter.RetryAfterSeconds("10.0.0.9"));
    }

    // ------------------------------------------------------------------
    // LoginFailureLimiter: 5/60с на (login_ci≤100, IP), путь отказа.
    // ------------------------------------------------------------------

    [Fact]
    public void LoginFailure_FifthFailureBlocksOnlySameLoginAndIp()
    {
        var time = new FakeTimeProvider();
        var limiter = new LoginFailureLimiter(time);

        for (var failure = 1; failure <= 5; failure++)
        {
            Assert.False(limiter.ShouldBlock("teacher", "10.0.0.1"));
            limiter.RegisterFailure("teacher", "10.0.0.1");
        }

        Assert.True(limiter.ShouldBlock("Teacher", "10.0.0.1"));
        Assert.False(limiter.ShouldBlock("other-login", "10.0.0.1"));
        Assert.False(limiter.ShouldBlock("teacher", "10.0.0.2"));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.False(limiter.ShouldBlock("teacher", "10.0.0.1"));
        Assert.Null(limiter.RetryAfterSeconds("teacher", "10.0.0.1"));
    }

    [Fact]
    public void LoginFailure_BlockCheckDoesNotExtendWindow()
    {
        var time = new FakeTimeProvider();
        var limiter = new LoginFailureLimiter(time);

        for (var failure = 0; failure < 5; failure++)
        {
            limiter.RegisterFailure("teacher", "10.0.0.1");
        }

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.True(limiter.ShouldBlock("teacher", "10.0.0.1"));
        Assert.True(limiter.ShouldBlock("teacher", "10.0.0.1"));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(limiter.ShouldBlock("teacher", "10.0.0.1"));
    }

    [Fact]
    public void LoginFailure_LoginLongerThan100_TruncatedToSameKey()
    {
        var time = new FakeTimeProvider();
        var limiter = new LoginFailureLimiter(time);

        var longLogin = new string('A', 1000);
        for (var failure = 0; failure < 5; failure++)
        {
            limiter.RegisterFailure(longLogin, "10.0.0.1");
        }

        // Ключ усечён до 100 символов ci-правилом — счёт вёлся по нему же:
        // 'A'×1000 и 'a'×100 нормализуются в один ключ; отличный в первых
        // 100 символах логин — другой счётчик.
        Assert.True(limiter.ShouldBlock(new string('a', 100), "10.0.0.1"));
        Assert.True(limiter.ShouldBlock(new string('a', 1000), "10.0.0.1"));
        Assert.False(limiter.ShouldBlock(new string('b', 101), "10.0.0.1"));
    }

    // ------------------------------------------------------------------
    // RecoveryRequestLimiter: 3/3600с на email_ci≤254, ВСЕ попытки.
    // ------------------------------------------------------------------

    [Fact]
    public void RecoveryRequest_ThirdRequestAllowed_FourthRejected_CiEquivalent()
    {
        var limiter = new RecoveryRequestLimiter(new FakeTimeProvider());

        Assert.True(limiter.TryAcquire("a@b.ru"));
        Assert.True(limiter.TryAcquire(" A@B.RU "));
        Assert.True(limiter.TryAcquire("a@b.ru"));
        Assert.False(limiter.TryAcquire("a@b.ru "));
    }

    [Fact]
    public void RecoveryRequest_EmailLongerThan254_Truncated_SameCounter()
    {
        var limiter = new RecoveryRequestLimiter(new FakeTimeProvider());

        var prefix = new string('a', 254);
        Assert.True(limiter.TryAcquire(prefix + new string('x', 99_746)));
        Assert.True(limiter.TryAcquire(prefix));
        Assert.True(limiter.TryAcquire(prefix + "y"));
        Assert.False(limiter.TryAcquire(prefix + new string('x', 99_746)));
    }

    [Fact]
    public void RecoveryRequest_NonStringOrMissingKey_EmptyStringCounter()
    {
        var limiter = new RecoveryRequestLimiter(new FakeTimeProvider());

        Assert.True(limiter.TryAcquire(null));
        Assert.True(limiter.TryAcquire(string.Empty));
        Assert.True(limiter.TryAcquire("   "));
        Assert.False(limiter.TryAcquire(null));
        Assert.Equal(1, limiter.TrackedKeysCount);
    }

    // ------------------------------------------------------------------
    // Прикладной сценарий потолка ключей и overflow: 10000 уникальных
    // ключей + корзина; при этом НОЛЬ операций KDF (FR-004: амплитуда CPU
    // recovery/request пренебрежима).
    // ------------------------------------------------------------------

    [Fact]
    public void RecoveryRequest_KeyCeiling_FirstTenThousandIndividual_ThenOverflowBucket_NoKdf()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(StartTime);
        var limiter = new RecoveryRequestLimiter(time);
        using var counter = TestsKdfCounter();

        // Первые 10000 уникальных ключей — индивидуальные счётчики, по 1 метке.
        for (var i = 0; i < 10_000; i++)
        {
            Assert.True(limiter.TryAcquire($"user{i}@example.com"), $"ключ {i}");
        }

        Assert.Equal(10_000, limiter.TrackedKeysCount);

        // 10001–10003 — первые три попадания overflow-корзины (лимит 3).
        Assert.True(limiter.TryAcquire("overflow-1@example.com"));
        Assert.True(limiter.TryAcquire("overflow-2@example.com"));
        Assert.True(limiter.TryAcquire("overflow-3@example.com"));

        // 10004-й новый ключ — корзина исчерпана (3 метки ≥ лимита).
        Assert.False(limiter.TryAcquire("overflow-4@example.com"));

        Assert.Equal(10_001, limiter.TrackedKeysCount);

        // Уровень лимитера: ни одного вызова KDF (счётчик не тронут вовсе).
        Assert.Empty(counter.Snapshot());
    }

    [Fact]
    public void RecoveryRequest_RetryAfterSeconds_ForOverflowedKey_ReportsBucket()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(StartTime);
        var limiter = new RecoveryRequestLimiter(time);

        for (var i = 0; i < 10_000; i++)
        {
            _ = limiter.TryAcquire($"user{i}@example.com");
        }

        Assert.True(limiter.TryAcquire("second@example.com"));  // корзина
        Assert.Null(limiter.RetryAfterSeconds("third@example.com")); // корзина пуста → нет ожидания

        Assert.True(limiter.TryAcquire("third@example.com"));   // корзина, 2-я метка
        Assert.True(limiter.TryAcquire("fourth@example.com"));  // корзина, 3-я метка (лимит 3)
        Assert.False(limiter.TryAcquire("fifth@example.com"));
        Assert.Equal(3600, limiter.RetryAfterSeconds("fifth@example.com"));
    }

    private static KdfCounter TestsKdfCounter()
    {
        var meter = new System.Diagnostics.Metrics.Meter("labs.api", "1.0");
        return new KdfCounter(meter, new FakeTimeProvider());
    }
}
