using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-016 «Движок лимитера: независимость ключей и ShouldBlock без побочных
/// эффектов» (negative, FR-003, P1).
///
/// given: ключ A заблокирован (5 меток в окне), ключ B без меток; политика
///        window=60000, limit=5; инжектируемые часы.
/// when:  TryAcquire(policy, B, now); ShouldBlock(policy, A, now) дважды; затем
///        TryAcquire(policy, A, now).
/// then:  TryAcquire(B)=true (ключи независимы); ShouldBlock(A)=true без
///        изменения состояния — число меток A по-прежнему 5, поэтому и
///        последующий TryAcquire(A)=false (ShouldBlock метку не пишет).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts013_LimiterKeysIndependent,
/// Ts017_ShouldBlockPureCheck) не изменялись.
/// </summary>
public sealed class Ts016_LimiterKeysIndependentShouldBlockPureTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 13_000_000;
    private const string KeyA = "ts016-independent-key-a";
    private const string KeyB = "ts016-independent-key-b";

    [Fact]
    public void ExhaustedKeyA_DoesNotBlockKeyB_AndShouldBlockWritesNoMark()
    {
        // given: ключ A заблокирован — 5 меток в окне; ключ B без меток.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(
                engine.TryAcquire(KeyA, WindowMs, Limit),
                $"Предусловие: метка {attempt + 1} ключа A должна допускаться.");
        }

        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));

        // when: TryAcquire(policy, B, now) — ключ B без меток.
        var bAllowed = engine.TryAcquire(KeyB, WindowMs, Limit);

        // then: true — исчерпание ключа A не переносится на ключ B
        // (ключи независимы).
        Assert.True(bAllowed, "Ключ B должен допускаться независимо от заблокированного ключа A.");

        // when: ShouldBlock(policy, A, now) дважды (проверка без записи).
        var blockFirst = engine.ShouldBlock(KeyA, WindowMs, Limit);
        var blockSecond = engine.ShouldBlock(KeyA, WindowMs, Limit);

        // then: оба вызова true; состояние не изменилось — меток A по-прежнему 5
        // (ShouldBlock метку не пишет).
        Assert.True(blockFirst, "Ключ A с 5 метками (= limit) должен блокировать.");
        Assert.True(blockSecond, "Повторная проверка того же ключа также блокирует.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));

        // when: затем TryAcquire(policy, A, now).
        var aRetry = engine.TryAcquire(KeyA, WindowMs, Limit);

        // then: false — ShouldBlock не изменил состояние (меток по-прежнему 5,
        // отказ и повторные проверки не дописывают метки).
        Assert.False(aRetry, "TryAcquire(A) после ShouldBlock должен оставаться отклонённым.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));
    }
}
