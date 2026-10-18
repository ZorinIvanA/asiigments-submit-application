using System.Collections.Concurrent;
using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-019 «Движок лимитера: потокобезопасность (32 параллельных TryAcquire)»
/// (concurrency, FR-003, P0).
///
/// given: политика limit=5; ключ K без меток; барьер старта 32 задач (все
///        вызовы в один и тот же момент инжектированных часов).
/// when:  32 параллельных TryAcquire(policy, K, now) одного ключа в один момент.
/// then:  ровно 5 вернули true, остальные false; исключений нет
///        (FR-003 AC «Потокобезопасность»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts019_LimiterParallelTryAcquireExactlyLimitTrueTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const int ParallelCalls = 32;
    private const long T0 = 26_000_000;
    private const string Key = "ts019-wave-parallel-key";

    [Fact]
    public void ThirtyTwoParallelTryAcquires_AllowExactlyLimit_WithoutExceptions()
    {
        // given: политика limit=5; ключ K без меток; часы закреплены в одном
        // моменте — все 32 вызова в один и тот же now.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);

        // when: 32 параллельных TryAcquire(policy, K, now) одного ключа
        // (барьер старта — Parallel.For с общим закреплённым временем).
        var results = new bool[ParallelCalls];
        var exceptions = new ConcurrentQueue<Exception>();
        Parallel.For(0, ParallelCalls, index =>
        {
            try
            {
                results[index] = engine.TryAcquire(Key, WindowMs, Limit);
            }
            catch (Exception exception)
            {
                exceptions.Enqueue(exception);
            }
        });

        // then: исключений нет; ровно 5 вернули true, остальные false; меток
        // ключа K — ровно limit.
        Assert.True(
            exceptions.IsEmpty,
            "Параллельные TryAcquire не должны бросать исключений, фактически: " +
            string.Join("; ", exceptions.Select(exception => exception.Message)));
        Assert.Equal(Limit, results.Count(static allowed => allowed));
        Assert.Equal(Limit, B09LimiterInspection.MarksCount(engine, Key));
    }
}
