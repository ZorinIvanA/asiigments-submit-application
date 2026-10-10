using System.Collections.Concurrent;
using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-016 «Лимитер: потокобезопасность — ровно limit допусков» (concurrency, FR-003, P0).
///
/// given: политика limit=5; ключ K без меток.
/// when:  32 параллельных вызова TryAcquire('test-policy', K, one_now) в один момент.
/// then:  ровно 5 вызовов вернули true; исключений нет (FR-003 AC «Потокобезопасность»).
/// </summary>
public sealed class Ts016_LimiterConcurrencyExactlyLimitTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const int ParallelCalls = 32;
    private const long T0 = 6_000_000;
    private const string Key = "ts016-key";

    [Fact]
    public void ParallelAcquires_AtOneMoment_AllowExactlyLimit()
    {
        // given: движок с политикой limit=5; ключ K без меток; часы закреплены
        // в одном моменте (one_now) — все вызовы видят одно и то же время.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);

        // when: 32 параллельных TryAcquire одного ключа.
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

        // then: исключений нет; ровно 5 вызовов вернули true; меток — ровно 5.
        Assert.True(
            exceptions.IsEmpty,
            $"Параллельные TryAcquire не должны бросать исключений, фактически: " +
            $"{string.Join("; ", exceptions.Select(exception => exception.Message))}");
        Assert.Equal(Limit, results.Count(static allowed => allowed));
        Assert.Equal(Limit, B09LimiterInspection.MarksCount(engine, Key));
    }
}
