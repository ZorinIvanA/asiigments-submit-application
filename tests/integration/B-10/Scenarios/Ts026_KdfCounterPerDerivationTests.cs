using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// Непараллельная коллекция KDF-кейсов батча B-10 (TS-026/TS-027, FR-005/NFR-004):
/// счётчик auth_kdf_operations_total — singleton тестового хоста (IKdfCounter,
/// замороженный тестовый шов ADR-031); DisableParallelization выносит измерения
/// в непараллельный прогон, чтобы лог записей не перемешивался с посторонними
/// деривациями других коллекций (деривации эталонных хэшей происходят при
/// материализации хоста — до окна Δ-измерения каждого кейса).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class B10KdfSerialCollection
{
    public const string Name = "B10 KDF serial";
}

/// <summary>
/// TS-026 «Счётчик инкрементируется на каждую деривацию» (nfr, FR-005/NFR-004, P0).
///
/// given: счётчик auth_kdf_operations_total обнулён (Δ-измерение по замороженному
///        шву IKdfCounter.Snapshot(): базовый снимок сумм по меткам вызывателя
///        снят до when-блока; хост материализован ДО снимка — деривации
///        эталонного хэша при старте хэшера в окно измерения не попадают).
/// when:  Hash(x,'register'); Verify(a,h,'login'); VerifyReference(b) —
///        эталонная деривация БЕЗ аргумента caller, с фиксированной меткой
///        'reference' (арбитраж a-039/CR-003: сигнатура IF-002 —
///        VerifyReference(password); в эталонную ветку CallerLogin не передавать).
/// then:  Сумма счётчика по всем меткам вызывателя = 3 — ровно по одной деривации
///        с метками register, login и reference; результат эталонной ветки false
///        (FR-005 AC «Счётчик на каждый вызов»; NFR-004).
/// </summary>
[Collection(B10KdfSerialCollection.Name)]
public sealed class Ts026_KdfCounterPerDerivationTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory;

    public Ts026_KdfCounterPerDerivationTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public void HashVerifyAndReferenceDerivations_IncrementCounterByExactlyThree()
    {
        // given: счётчик «обнулён» (Δ-измерение по шву IKdfCounter — базовый снимок
        // до первой деривации кейса; хэшер/счётчик разрешены ДО снимка: деривация
        // эталонного хэша при конструировании singleton'а в окно не попадает).
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = _factory.Services.GetRequiredService<IKdfCounter>();
        var before = counter.Snapshot();

        // when: Hash(x,'register'); Verify(a,h,'login'); VerifyReference(b).
        var password = "Str0ng!pass-ts026";
        var storedHash = B10KdfContract.Hash(hasher, password, B10KdfContract.CallerRegister);
        var verified = B10KdfContract.Verify(hasher, password, storedHash, B10KdfContract.CallerLogin);
        var referenceResult = B10KdfContract.VerifyReference(hasher, "ts026-unrelated-secret");

        // then: результат эталонной ветки false (FR-005: результат всегда
        // отбрасывается); Verify корректного пароля — true (предусловие кейса).
        Assert.True(
            verified,
            "Предусловие: Verify корректного пароля против Hash(x) должен вернуть true.");
        Assert.False(
            referenceResult,
            "Эталонная проверка всегда отбрасывается — результат false (FR-005).");

        // then: сумма счётчика по всем меткам вызывателя = 3 — ровно по одной
        // деривации с метками register, login и reference (FR-005 AC «Счётчик на
        // каждый вызов», NFR-004).
        var after = counter.Snapshot();
        var deltas = after.ToDictionary(
            pair => pair.Key,
            pair => pair.Value - before.GetValueOrDefault(pair.Key));
        var deltaTotal = deltas.Values.Sum();

        Assert.True(
            deltaTotal == 3,
            $"Сумма счётчика auth_kdf_operations_total по всем меткам вызывателя должна " +
            $"увеличиться ровно на 3, фактически +{deltaTotal} (разбивка: {Describe(deltas)}).");
        foreach (var (caller, expected) in new[]
                 {
                     (KdfCallers.Register, 1L),
                     (KdfCallers.Login, 1L),
                     (KdfCallers.Reference, 1L),
                 })
        {
            Assert.True(
                deltas.GetValueOrDefault(caller) == expected,
                $"Ожидается ровно {expected} деривации с меткой «{caller}», фактически " +
                $"{deltas.GetValueOrDefault(caller)} (разбивка: {Describe(deltas)}).");
        }

        foreach (var caller in KdfCallers.All)
        {
            if (caller is KdfCallers.Register or KdfCallers.Login or KdfCallers.Reference)
            {
                continue;
            }

            Assert.True(
                deltas.GetValueOrDefault(caller) == 0,
                $"Метка «{caller}» не должна учитываться в кейсе TS-026, фактически " +
                $"{deltas.GetValueOrDefault(caller)} (разбивка: {Describe(deltas)}).");
        }
    }

    /// <summary>Текстовая разбивка дельт по меткам — диагностика сообщения об отказе.</summary>
    private static string Describe(IReadOnlyDictionary<string, long> deltas) =>
        string.Join(
            ", ",
            deltas.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}=+{pair.Value}"));
}
