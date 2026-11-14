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
///        изменения состояния — после двух вызовов TryAcquire(A) по-прежнему
///        false (ShouldBlock метку не пишет).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts016_LimiterKeyIsolationPureShouldBlockTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 23_000_000;
    private const string KeyA = "ts016-wave-key-a";
    private const string KeyB = "ts016-wave-key-b";

    [Fact]
    public void KeyIsolation_Holds_AndShouldBlockLeavesStateUnchanged()
    {
        // given: ключ A заблокирован — 5 меток в окне; ключ B без меток.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.True(
                engine.TryAcquire(KeyA, WindowMs, Limit),
                $"Предусловие: метка {attempt + 1} ключа A должна допускаться.");
        }

        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));

        // when: TryAcquire(policy, B, now) — ключ B без меток.
        var bAllowed = engine.TryAcquire(KeyB, WindowMs, Limit);

        // then: true — ключи независимы (блокировка A не переносится на B).
        Assert.True(bAllowed, "Ключ B должен допускаться независимо от исчерпанного ключа A.");

        // when: ShouldBlock(policy, A, now) дважды — проверка без записи.
        var blockedFirst = engine.ShouldBlock(KeyA, WindowMs, Limit);
        var blockedSecond = engine.ShouldBlock(KeyA, WindowMs, Limit);

        // then: оба true — ключ A исчерпан; состояние не изменилось (меток A
        // по-прежнему 5 — ShouldBlock метку не пишет).
        Assert.True(blockedFirst, "Ключ A с 5 метками (= limit) должен блокироваться.");
        Assert.True(blockedSecond, "Повторный ShouldBlock того же ключа также true.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));

        // when: затем TryAcquire(policy, A, now).
        var aRetry = engine.TryAcquire(KeyA, WindowMs, Limit);

        // then: false — после двух ShouldBlock ключ A по-прежнему отклонён
        // (ShouldBlock не изменил состояние).
        Assert.False(aRetry, "TryAcquire(A) после ShouldBlock обязан остаться отклонённым.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, KeyA));
    }
}
