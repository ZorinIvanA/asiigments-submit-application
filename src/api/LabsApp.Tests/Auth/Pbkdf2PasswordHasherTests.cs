using System.Diagnostics.Metrics;
using System.Globalization;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки IPasswordHasher (IF-002, FR-005, AR-004): формат
/// «pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;», roundtrip,
/// параметры из САМОГО хэша (смена конфигурации не ломает старые хэши),
/// malformed storedHash → false БЕЗ KDF и БЕЗ инкремента, VerifyReference —
/// ровно одна деривация против эталона при старте (метка reference, результат
/// отбрасывается), счётчик инкрементируется КАЖДОЙ деривацией.
/// </summary>
public sealed class Pbkdf2PasswordHasherTests
{
    /// <summary>Тестовые итерации (FR-027: малые итерации в тестах).</summary>
    private const int TestIterations = 1000;

    [Theory]
    [InlineData("teacher123!")]
    [InlineData("Passw0rd!")]
    [InlineData("1")]
    [InlineData("пароль-с-кириллицей")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Hash_Verify_Roundtrip(string password)
    {
        var hasher = CreateHasher(TestIterations);

        Assert.True(hasher.Verify(password, hasher.Hash(password, KdfCallers.Register), KdfCallers.Login));
    }

    [Fact]
    public void Hash_Format_Pbkdf2Sha256_WithConfiguredIterations()
    {
        // AC: формат хэша 'pbkdf2-sha256$1000$…' при итерациях 1000.
        var hasher = CreateHasher(TestIterations);
        var hash = hasher.Hash("Passw0rd!", KdfCallers.Register);

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal(TestIterations.ToString(CultureInfo.InvariantCulture), parts[1]);
        Assert.Equal(Pbkdf2PasswordHasher.SaltSizeBytes, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(Pbkdf2PasswordHasher.HashSizeBytes, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_SameInput_UniqueSalts_DifferentHashes()
    {
        var hasher = CreateHasher(TestIterations);

        var first = hasher.Hash("same-password", KdfCallers.Register);
        var second = hasher.Hash("same-password", KdfCallers.Register);

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("same-password", first, KdfCallers.Login));
        Assert.True(hasher.Verify("same-password", second, KdfCallers.Login));
    }

    [Fact]
    public void Verify_WrongPassword_False()
    {
        var hasher = CreateHasher(TestIterations);
        var hash = hasher.Hash("right-password", KdfCallers.Register);

        Assert.False(hasher.Verify("wrong-password", hash, KdfCallers.Login));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$1000$AAAA$AAAA")]
    [InlineData("PBKDF2-SHA256|1000|AAAAAAAAAAAAAAAAAAAAAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("$pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("md5$1000$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$notanumber$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$0$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("pbkdf2-sha256$1000$!!!не-base64!!!$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1000$AAAAAAAAAAAAAAAAAAAAAA$!!!не-base64!!!")]
    [InlineData("pbkdf2-sha256$1000$AAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Verify_MalformedStoredHash_False_WithoutKdfAndIncrement(string stored)
    {
        // IF-002 errors.malformed_stored: false БЕЗ выполнения KDF и БЕЗ инкремента.
        var (hasher, _, counter) = Create(TestIterations);
        var baseline = counter.Snapshot().Values.Sum();

        Assert.False(hasher.Verify("any", stored, KdfCallers.Login));
        Assert.Equal(0, counter.Snapshot().Values.Sum() - baseline);
    }

    [Fact]
    public void Verify_PasswordLongerThan128_FalseImmediately_WithoutKdf()
    {
        var (hasher, _, counter) = Create(TestIterations);
        var hash = hasher.Hash("short", KdfCallers.Register);
        var baseline = counter.Snapshot().Values.Sum();
        var tooLong = new string('a', AuthCoreDefaults.PasswordMaxLength + 1);

        Assert.False(hasher.Verify(tooLong, hash, KdfCallers.Login));
        Assert.Equal(0, counter.Snapshot().Values.Sum() - baseline);
    }

    [Fact]
    public void Verify_AtBoundary128_StillVerifies()
    {
        var hasher = CreateHasher(TestIterations);
        var boundary = new string('a', AuthCoreDefaults.PasswordMaxLength);

        Assert.True(hasher.Verify(boundary, hasher.Hash(boundary, KdfCallers.Seed), KdfCallers.Login));
    }

    [Fact]
    public void VerifyReference_PasswordLongerThan128_FalseImmediately_WithoutKdf()
    {
        // IF-002 guarantees: кандидат длиннее 128 символов (ASM-015) отклоняется
        // и в эталонной ветке — false БЕЗ деривации и БЕЗ инкремента счётчика.
        var (hasher, _, counter) = Create(TestIterations);
        var baseline = counter.Snapshot().Values.Sum();
        var tooLong = new string('a', AuthCoreDefaults.PasswordMaxLength + 1);

        Assert.False(hasher.VerifyReference(tooLong));
        Assert.Equal(0, counter.Snapshot().Values.Sum() - baseline);
    }

    [Fact]
    public void Verify_EmptyArguments_False()
    {
        var hasher = CreateHasher(TestIterations);
        var hash = hasher.Hash("pwd", KdfCallers.Register);

        Assert.False(hasher.Verify(string.Empty, hash, KdfCallers.Login));
        Assert.False(hasher.Verify("pwd", string.Empty, KdfCallers.Login));
        Assert.False(hasher.Verify("pwd", hash, string.Empty));
    }

    [Fact]
    public void VerifyReference_ReturnsFalse_AndIncrementsExactlyOnce()
    {
        // AC FR-005 «Эталонная проверка считается»: VerifyReference → false;
        // счётчик{reference} инкрементируется ровно на 1 за вызов.
        var (hasher, _, counter) = Create(TestIterations);

        // Эталон создан при старте: ровно одна деривация с меткой reference.
        Assert.Equal(1, counter.Snapshot()[KdfCallers.Reference]);

        var baseline = counter.Snapshot()[KdfCallers.Reference];
        Assert.False(hasher.VerifyReference("anything"));
        Assert.False(hasher.VerifyReference("teacher123!"));
        Assert.Equal(baseline + 2, counter.Snapshot()[KdfCallers.Reference]);
        Assert.Equal(0, counter.Snapshot().Where(kv => kv.Key != KdfCallers.Reference).Sum(kv => kv.Value));
    }

    [Fact]
    public void CounterIncrement_PerMethodPerLabel_ExactlyOneDerivationEach()
    {
        // Δсчётчика по меткам на каждый метод хэшера (unit-требования T-004):
        // Hash и Verify инкрементируют метку СВОЕГО вызывателя ровно на 1,
        // VerifyReference — метку reference (эталонная деривация старта учтена
        // отдельно и не входит в дельту методов).
        var options = new AuthOptions { Pbkdf2Iterations = TestIterations };
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());
        var hasher = new Pbkdf2PasswordHasher(Options.Create(options), counter);

        var afterCtor = counter.Snapshot();
        Assert.Equal(1, afterCtor[KdfCallers.Reference]);

        var hash = hasher.Hash("pwd", KdfCallers.Seed);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.Seed]);
        Assert.Equal(afterCtor[KdfCallers.Reference], counter.Snapshot()[KdfCallers.Reference]);

        _ = hasher.Verify("pwd", hash, KdfCallers.ChangePassword);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.ChangePassword]);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.Seed]);

        _ = hasher.VerifyReference("pwd");
        Assert.Equal(afterCtor[KdfCallers.Reference] + 1, counter.Snapshot()[KdfCallers.Reference]);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.Seed]);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.ChangePassword]);
    }

    [Fact]
    public void KdfCounterScenario_HashVerifyVerifyReference_SumIsThree()
    {
        // AC подзадачи «KDF-счётчик»: Hash(x); Verify(a,h); VerifyReference(b)
        // → сумма по меткам = 3 (счётчик «обнулён» снимком после старта);
        // VerifyReference вернул false; формат 'pbkdf2-sha256$1000$…'.
        var (hasher, _, counter) = Create(TestIterations);
        var baseline = counter.Snapshot().Values.Sum();

        var hash = hasher.Hash("x", KdfCallers.Register);
        _ = hasher.Verify("a", hash, KdfCallers.Login);
        var referenceResult = hasher.VerifyReference("b");

        Assert.False(referenceResult);
        Assert.StartsWith("pbkdf2-sha256$1000$", hash, StringComparison.Ordinal);
        Assert.Equal(3, counter.Snapshot().Values.Sum() - baseline);
    }

    [Fact]
    public void ParamsFromHash_OldHashVerifiesAfterConfigChange_NewHashesUseNewIterations()
    {
        // AC подзадачи «Параметры из хэша»: пользователь создан при 1000,
        // конфигурация изменена на 2000 → Verify старым паролем — true
        // (использованы 1000 из хэша); новый Hash — с 2000.
        var options = new AuthOptions { Pbkdf2Iterations = TestIterations };
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());
        var hasher = new Pbkdf2PasswordHasher(Options.Create(options), counter);

        var oldHash = hasher.Hash("Str0ng!pass", KdfCallers.Register);

        options.Pbkdf2Iterations = 2000;

        // Если бы Verify использовал конфигурацию (2000) вместо хэша — false.
        Assert.True(hasher.Verify("Str0ng!pass", oldHash, KdfCallers.Login));
        Assert.False(hasher.Verify("wrong", oldHash, KdfCallers.Login));

        var newHash = hasher.Hash("Str0ng!pass", KdfCallers.ChangePassword);
        Assert.StartsWith("pbkdf2-sha256$2000$", newHash, StringComparison.Ordinal);
        Assert.True(hasher.Verify("Str0ng!pass", newHash, KdfCallers.Login));
    }

    [Fact]
    public void LegacyHash_FailedLoginAndReferenceBranch_EachExactlyOneDerivation()
    {
        // AC FR-005 (AR-004): legacy-хэш (создан при 1000, конфигурация теперь
        // 2000): ветка Verify и ветка VerifyReference — Δkdf=1 на каждый запрос.
        var options = new AuthOptions { Pbkdf2Iterations = TestIterations };
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());
        var hasher = new Pbkdf2PasswordHasher(Options.Create(options), counter);

        var legacyHash = hasher.Hash("Str0ng!pass", KdfCallers.Register);
        options.Pbkdf2Iterations = 2000;
        var baseline = counter.Snapshot().Values.Sum();

        Assert.False(hasher.Verify("wrong", legacyHash, KdfCallers.Login));
        Assert.False(hasher.VerifyReference("wrong"));

        // Δkdf=1 на каждый из двух запросов (в т.ч. метка login — ровно 1).
        Assert.Equal(2, counter.Snapshot().Values.Sum() - baseline);
        Assert.Equal(1, counter.Snapshot()[KdfCallers.Login]);
        Assert.Equal(2, counter.Snapshot()[KdfCallers.Reference]);
    }

    [Fact]
    public void Hash_EmptyPasswordOrNullCaller_Throws()
    {
        var hasher = CreateHasher(TestIterations);

        Assert.Throws<ArgumentException>(() => hasher.Hash(string.Empty, KdfCallers.Register));
        Assert.Throws<ArgumentNullException>(() => hasher.Hash("pwd", null!));
    }

    [Fact]
    public void Constructor_InvalidIterationsConfiguration_Throws()
    {
        // Fail-fast: эталонный хэш (AR-004/SEC-001) выводится в КОНСТРУКТОРЕ
        // (ровно одна деривация при старте), поэтому дефект конфигурации
        // итераций обнаруживается при построении хэшера, а не при первом Hash.
        // В реальном хосте конфиг отсеивает AuthOptionsValidator (любое
        // окружение) — здесь защитный отказ singleton'а.
        var options = new AuthOptions { Pbkdf2Iterations = 0 };
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        Assert.Throws<InvalidOperationException>(
            () => new Pbkdf2PasswordHasher(Options.Create(options), counter));
    }

    // ------------------------------------------------------------------

    private static Pbkdf2PasswordHasher CreateHasher(int iterations) =>
        new(Options.Create(new AuthOptions { Pbkdf2Iterations = iterations }), CreateCounter());

    private static KdfCounter CreateCounter()
    {
        var meter = new Meter("labs.api", "1.0");
        return new KdfCounter(meter, new FakeTimeProvider());
    }

    private static (Pbkdf2PasswordHasher Hasher, AuthOptions Options, KdfCounter Counter) Create(int iterations)
    {
        var options = new AuthOptions { Pbkdf2Iterations = iterations };
        var counter = CreateCounter();
        var hasher = new Pbkdf2PasswordHasher(Options.Create(options), counter);
        return (hasher, options, counter);
    }
}
