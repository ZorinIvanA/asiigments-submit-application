using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// In-memory ILoggerProvider (log-sink, ADR-010/ADR-018). Единственный владелец —
/// тестовая инфраструктура наблюдаемости (T-004, компонент C-012); T-022/T-023 и
/// доменные проверки NFR-005/NFR-008 только переиспользуют его, дублирование
/// запрещено (ISS-001). Собирает записи ВСЕХ категорий (уровень, категория,
/// отформатированное сообщение, состояние/ключи); собственных фильтров sink не
/// применяет — поток записей определяется правилами уровня тестового хоста.
/// Потокобезопасен (запись из параллельных вызовов ILogger). Изоляция тестов —
/// Clear() в конструкторе тестового класса: тест видит только свои записи.
/// </summary>
public sealed class TestLogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<TestLogRecord> _records = [];
    private bool _disposed;

    /// <summary>Количество собранных записей.</summary>
    public int Count
    {
        get { lock (_gate) return _records.Count; }
    }

    /// <summary>Копия собранных записей на момент вызова; последующие записи и Clear её не меняют.</summary>
    public IReadOnlyList<TestLogRecord> Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    /// <summary>Очистка между тестами (AC «Изоляция тестов»).</summary>
    public void Clear()
    {
        lock (_gate) _records.Clear();
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new SinkLogger(this, categoryName);
    }

    void IDisposable.Dispose()
    {
        lock (_gate) _disposed = true;
    }

    private void Add(TestLogRecord record)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _records.Add(record);
        }
    }

    private sealed class SinkLogger(TestLogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter is null
                ? state?.ToString() ?? string.Empty
                : formatter(state, exception);

            string? template = null;
            var pairs = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            {
                foreach (var pair in structured)
                {
                    if (pair.Key == "{OriginalFormat}")
                    {
                        template = pair.Value as string;
                        continue;
                    }

                    pairs[pair.Key] = pair.Value;
                }
            }

            sink.Add(new TestLogRecord(
                category,
                logLevel,
                eventId,
                message,
                template,
                exception,
                DateTimeOffset.UtcNow,
                pairs));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
