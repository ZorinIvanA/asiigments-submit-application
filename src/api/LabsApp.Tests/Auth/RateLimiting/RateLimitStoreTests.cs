using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth.RateLimiting;

/// <summary>
/// Юнит-проверки состояния лимитера (IF-006/IF-015, FR-024, ADR-026):
/// семантика IRateLimitStore (TrackedKeysCount(policy) — инспекция для тестов,
/// TryGetMarks/GetOrAddMarks/Remove/GetKeys), изоляция политик в общем
/// экземпляре хранилища, проводка движка и прикладных лимитеров через
/// инжектируемое хранилище, NFR-003-потолок по каждой политике отдельно.
/// </summary>
public sealed class RateLimitStoreTests
{
    private const long Window = 60_000;
    private const int Limit = 5;

    // ------------------------------------------------------------------
    // Семантика операций хранилища.
    // ------------------------------------------------------------------

    [Fact]
    public void TryGetMarks_AbsentKey_False_WithoutCreation()
    {
        var store = new InMemoryRateLimitStore();

        Assert.False(store.TryGetMarks("login", "k", out var marks));
        Assert.NotNull(marks);
        Assert.Empty(marks);
        Assert.Equal(0, store.TrackedKeysCount("login"));
    }

    [Fact]
    public void GetOrAddMarks_CreatesEntryOnce_ReturnsSameList()
    {
        var store = new InMemoryRateLimitStore();

        var first = store.GetOrAddMarks("register", "10.0.0.1");
        var second = store.GetOrAddMarks("register", "10.0.0.1");

        Assert.Same(first, second);
        Assert.True(store.TryGetMarks("register", "10.0.0.1", out var marks));
        Assert.Same(first, marks);
        Assert.Equal(1, store.TrackedKeysCount("register"));

        // Мутация полученного списка видна хранилищу (движок дописывает метки).
        first.Add(42);
        Assert.True(store.TryGetMarks("register", "10.0.0.1", out var reloaded));
        Assert.Equal(42, reloaded[0]);
    }

    [Fact]
    public void Remove_RemovesOnlyExistingEntry_OfOwnPolicy()
    {
        var store = new InMemoryRateLimitStore();
        store.GetOrAddMarks("login", "k");
        store.GetOrAddMarks("register", "k");

        Assert.False(store.Remove("login", "absent"));
        Assert.True(store.Remove("login", "k"));
        Assert.False(store.Remove("login", "k"));
        Assert.Equal(0, store.TrackedKeysCount("login"));
        Assert.Equal(1, store.TrackedKeysCount("register"));
    }

    [Fact]
    public void GetKeys_ReturnsSnapshotCopy()
    {
        var store = new InMemoryRateLimitStore();
        store.GetOrAddMarks("recovery_request", "a@b.ru");
        store.GetOrAddMarks("recovery_request", "c@d.ru");

        var keys = store.GetKeys("recovery_request");
        Assert.Equal(2, keys.Count);

        // Снимок не мутируется последующими изменениями состояния.
        store.GetOrAddMarks("recovery_request", "e@f.ru");
        Assert.Equal(2, keys.Count);
        Assert.Equal(3, store.TrackedKeysCount("recovery_request"));
        Assert.Equal(3, store.GetKeys("recovery_request").Count);
    }

    [Fact]
    public void Operations_NullArguments_Throw()
    {
        IRateLimitStore store = new InMemoryRateLimitStore();

        Assert.Throws<ArgumentNullException>(() => store.TrackedKeysCount(null!));
        Assert.Throws<ArgumentNullException>(() => store.TryGetMarks(null!, "k", out _));
        Assert.Throws<ArgumentNullException>(() => store.TryGetMarks("login", null!, out _));
        Assert.Throws<ArgumentNullException>(() => store.GetOrAddMarks("login", null!));
        Assert.Throws<ArgumentNullException>(() => store.Remove("login", null!));
        Assert.Throws<ArgumentNullException>(() => store.GetKeys(null!));
    }

    // ------------------------------------------------------------------
    // Изоляция политик в общем экземпляре хранилища (матрица FR-004).
    // ------------------------------------------------------------------

    [Fact]
    public void SharedStore_PoliciesAreIsolated()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var register = new SlidingWindowLimiter(time, store, RateLimitPolicies.Register);
        var login = new SlidingWindowLimiter(time, store, RateLimitPolicies.Login);

        Assert.True(register.TryAcquire("shared-key", Window, Limit));
        Assert.True(register.TryAcquire("shared-key", Window, Limit));

        // Тот же ключ в другой политике — другой словарь и другой счёт.
        Assert.True(login.TryAcquire("shared-key", Window, Limit));
        Assert.True(store.TryGetMarks(RateLimitPolicies.Register, "shared-key", out var registerMarks));
        Assert.Equal(2, registerMarks.Count);
        Assert.True(store.TryGetMarks(RateLimitPolicies.Login, "shared-key", out var loginMarks));
        Assert.Single(loginMarks);
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Register));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Login));
        Assert.Equal(1, register.TrackedKeysCount);
        Assert.Equal(1, login.TrackedKeysCount);

        // Доводим счёт ключа в политике регистраций до лимита (уже 2 метки):
        // отказ регистраций не влияет на политику входов.
        for (var attempt = 0; attempt < Limit - 2; attempt++)
        {
            Assert.True(register.TryAcquire("shared-key", Window, Limit));
        }

        Assert.False(register.TryAcquire("shared-key", Window, Limit));
        Assert.False(login.ShouldBlock("shared-key", Window, Limit));
    }

    [Fact]
    public void SharedStore_PerPolicyCeiling_Nfr003()
    {
        // NFR-003 действует на каждую политику отдельно: 10000 индивидуальных
        // ключей + корзина одной политики; другая политика не занята.
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var register = new SlidingWindowLimiter(time, store, RateLimitPolicies.Register);

        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(register.TryAcquire($"key-{i}", Window, Limit));
        }

        Assert.True(register.TryAcquire("K10001", Window, Limit));
        Assert.True(register.TryAcquire("K10002", Window, Limit));

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1,
            store.TrackedKeysCount(RateLimitPolicies.Register));
        Assert.Contains(SlidingWindowLimiter.OverflowKeyName,
            store.GetKeys(RateLimitPolicies.Register));
        Assert.Equal(0, store.TrackedKeysCount(RateLimitPolicies.Login));
        Assert.Equal(0, store.TrackedKeysCount(RateLimitPolicies.RecoveryRequest));
    }

    // ------------------------------------------------------------------
    // Проводка: прикладные лимитеры адресуют состояние именами политик.
    // ------------------------------------------------------------------

    [Fact]
    public void ApplicationLimiters_UseInjectedStore_WithOwnPolicyNames()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var register = new RegisterLimiter(time, store);
        var loginFailures = new LoginFailureLimiter(time, store);
        var recovery = new RecoveryRequestLimiter(time, store);

        Assert.True(register.TryAcquire("10.0.0.1"));
        loginFailures.RegisterFailure("teacher", "10.0.0.1");
        Assert.True(recovery.TryAcquire("a@b.ru"));

        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Register));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Login));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.RecoveryRequest));
        Assert.Equal(1, register.TrackedKeysCount);

        // Вычистка выполняется при проверке СВОЕЙ политики: окно регистраций
        // вычищается его проверкой, а нетронутая политика входов хранит запись
        // (безвредно: потолок NFR-003 снимается вычисткой при её проверке).
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(register.TryAcquire("10.0.0.2"));
        Assert.Equal(2, store.TrackedKeysCount(RateLimitPolicies.Register));
        Assert.Equal(1, store.TrackedKeysCount(RateLimitPolicies.Login));

        // Проверка политики входов вычищает её устаревшую запись.
        Assert.False(loginFailures.ShouldBlock("teacher", "10.0.0.1"));
        Assert.Equal(0, store.TrackedKeysCount(RateLimitPolicies.Login));
    }

    [Fact]
    public void BareEngines_DefaultCtor_AreIsolatedFromEachOther()
    {
        var time = new FakeTimeProvider();
        var first = new SlidingWindowLimiter(time);
        var second = new SlidingWindowLimiter(time);

        Assert.True(first.TryAcquire("k", Window, Limit));
        Assert.Equal(1, first.TrackedKeysCount);
        Assert.Equal(0, second.TrackedKeysCount);
    }

    [Fact]
    public void Engine_Constructor_Guards()
    {
        var time = new FakeTimeProvider();
        Assert.Throws<ArgumentNullException>(() => new SlidingWindowLimiter(null!));
        Assert.Throws<ArgumentNullException>(
            () => new SlidingWindowLimiter(time, null!, RateLimitPolicies.Login));
        Assert.ThrowsAny<ArgumentException>(
            () => new SlidingWindowLimiter(time, new InMemoryRateLimitStore(), " "));
    }
}
