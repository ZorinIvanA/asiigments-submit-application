using System.Diagnostics.Metrics;
using LabsApp.Auth;
using LabsApp.Tests.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth;

/// <summary>
/// Юнит-проверки IKdfCounter (IF-002, NFR-004): КАЖДАЯ деривация инкрементирует
/// счётчик ровно на 1 с меткой вызывателя; Snapshot — копия (тестовый шов);
/// метрика auth_kdf_operations_total{caller}; суммарное значение логируется
/// раз в 60 с — при FakeTimeProvider тик детерминирован.
/// </summary>
public sealed class KdfCounterTests
{
    [Fact]
    public void Increment_EachCallAddsExactlyOne_PerLabel()
    {
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        counter.Increment(KdfCallers.Login);
        counter.Increment(KdfCallers.Login);
        counter.Increment(KdfCallers.Register);
        counter.Increment(KdfCallers.Reference);

        var snapshot = counter.Snapshot();
        Assert.Equal(2, snapshot[KdfCallers.Login]);
        Assert.Equal(1, snapshot[KdfCallers.Register]);
        Assert.Equal(1, snapshot[KdfCallers.Reference]);
        Assert.Equal(4, snapshot.Values.Sum());
    }

    [Fact]
    public void Snapshot_EmptyBeforeFirstIncrement_CopySemantics()
    {
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        Assert.Empty(counter.Snapshot());

        // Snapshot возвращает КОПИЮ: мутация результата не влияет на счётчик.
        var snapshot = counter.Snapshot();
        ((Dictionary<string, long>)snapshot)[KdfCallers.Seed] = 100;

        Assert.Empty(counter.Snapshot());
    }

    [Fact]
    public void Increment_SumAcrossKnownCallerLabels()
    {
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        foreach (var caller in KdfCallers.All)
        {
            counter.Increment(caller);
        }

        var snapshot = counter.Snapshot();
        Assert.Equal(KdfCallers.All.Count, snapshot.Count);
        Assert.All(KdfCallers.All, caller => Assert.Equal(1, snapshot[caller]));
        Assert.Equal(6, snapshot.Values.Sum());
    }

    [Fact]
    public void Increment_EmitsMetric_WithCallerTag()
    {
        // Уникальное имя метра: TestMeterListener подписывается на инструменты
        // ПО ИМЕНИ метра, а параллельные тестовые классы держат собственные
        // Meter «labs.api» — общий ресурс собирал бы чужие инкременты тех же
        // меток (недетерминированная сумма). Изолированный метр — только свои.
        var meterName = $"labs.api.kdf-{Guid.NewGuid():N}";
        using var meter = new Meter(meterName, "1.0");
        using var listener = new TestMeterListener(meterName, KdfCounter.MetricName);
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        counter.Increment(KdfCallers.Seed);
        counter.Increment(KdfCallers.Seed);
        counter.Increment(KdfCallers.Login);

        Assert.Equal(2, listener.CounterIncrement(
            KdfCounter.MetricName, (KdfCounter.MetricCallerTag, KdfCallers.Seed)));
        Assert.Equal(1, listener.CounterIncrement(
            KdfCounter.MetricName, (KdfCounter.MetricCallerTag, KdfCallers.Login)));
    }

    [Fact]
    public void PeriodicLog_After60Seconds_ReportsTotalSum()
    {
        // NFR-004: суммарное значение логируется раз в 60 с; тик по
        // FakeTimeProvider детерминирован.
        var time = new FakeTimeProvider();
        using var meter = new Meter("labs.api", "1.0");
        var sink = new CapturingLogger();
        using var counter = new KdfCounter(meter, time, sink);

        counter.Increment(KdfCallers.ChangePassword);
        counter.Increment(KdfCallers.ResetPassword);
        counter.Increment(KdfCallers.ResetPassword);

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Empty(sink.Records);

        time.Advance(TimeSpan.FromSeconds(1));
        var record = Assert.Single(sink.Records);
        Assert.Contains("3", record.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Information, record.Level);

        // Следующий период — суммарное значение (без новых операций — то же).
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(2, sink.Records.Count);
        Assert.Contains("3", sink.Records[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_StopsPeriodicLog()
    {
        var time = new FakeTimeProvider();
        using var meter = new Meter("labs.api", "1.0");
        var sink = new CapturingLogger();
        using var counter = new KdfCounter(meter, time, sink);

        counter.Dispose();
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.Empty(sink.Records);
    }

    [Fact]
    public void Increment_NullOrEmptyCaller_Throws()
    {
        using var meter = new Meter("labs.api", "1.0");
        using var counter = new KdfCounter(meter, new FakeTimeProvider());

        Assert.Throws<ArgumentException>(() => counter.Increment(string.Empty));
        Assert.Throws<ArgumentNullException>(() => counter.Increment(null!));
    }

    private sealed class CapturingLogger : ILogger<KdfCounter>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, formatter(state, exception)));
    }
}
