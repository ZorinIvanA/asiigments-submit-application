using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabsApp.Auth;

/// <summary>
/// Реализация <see cref="IKdfCounter"/> (IF-002, NFR-004): потокобезопасный
/// накопитель по меткам вызывателя + метрика auth_kdf_operations_total{caller}
/// (IF-016) + фоновый логер суммарного значения раз в 60 с через
/// TimeProvider.CreateTimer (ADR-002: единственный источник времени; в тестах
/// FakeTimeProvider детерминированно зажигает тик). В записях лога — только
/// счётчики, никаких секретов (NFR-006). Таймер останавливается при Dispose
/// singleton'а контейнером; колбэк никогда не бросает исключений.
/// </summary>
public sealed class KdfCounter : IKdfCounter, IDisposable
{
    /// <summary>Имя метрики KDF-операций (IF-016).</summary>
    public const string MetricName = "auth_kdf_operations_total";

    /// <summary>Имя метки вызывателя в метрике (IF-016: auth_kdf_operations_total{caller}).</summary>
    public const string MetricCallerTag = "caller";

    /// <summary>Период фонового лога суммарного значения (NFR-004: раз в 60 с).</summary>
    public static readonly TimeSpan LogPeriod = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _totals = new(StringComparer.Ordinal);
    private readonly Counter<long> _metric;
    private readonly ILogger _logger;
    private readonly ITimer _timer;
    private bool _disposed;

    public KdfCounter(Meter meter, TimeProvider timeProvider, ILogger<KdfCounter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _metric = meter.CreateCounter<long>(MetricName);
        _logger = logger ?? NullLogger<KdfCounter>.Instance;
        _timer = timeProvider.CreateTimer(OnTimerTick, null, LogPeriod, LogPeriod);
    }

    /// <inheritdoc/>
    public void Increment(string caller)
    {
        ArgumentException.ThrowIfNullOrEmpty(caller);

        lock (_gate)
        {
            _totals[caller] = _totals.GetValueOrDefault(caller) + 1;
        }

        _metric.Add(1, new KeyValuePair<string, object?>(MetricCallerTag, caller));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, long>(_totals, StringComparer.Ordinal);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
        }
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            var snapshot = Snapshot();
            long total = 0;
            foreach (var value in snapshot.Values)
            {
                total += value;
            }

            // NFR-004/IF-016: суммарное значение ПО МЕТКАМ вызывателя — запись
            // несёт метрику auth_kdf_operations_total, суммарное значение и
            // ненулевую разбивку по меткам в структурированном состоянии
            // (NFR-006: только счётчики и имена меток, никаких секретов).
            var logState = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [MetricName] = total,
            };
            foreach (var (caller, value) in snapshot)
            {
                logState[$"caller.{caller}"] = value;
            }

            _logger.Log(
                LogLevel.Information,
                new EventId(0, nameof(OnTimerTick)),
                logState,
                null,
                (_, _) =>
                    $"Суммарное число операций KDF {MetricName} с метками вызывателя: {total}.");
        }
        catch
        {
            // Фоновый тик наблюдаемости не может уронить процесс.
        }
    }
}
