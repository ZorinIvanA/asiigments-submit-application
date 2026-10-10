using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>Одна запись журнала, собранная <see cref="B20LogSink"/>.</summary>
public sealed record B20LogRecord(
    string Category,
    LogLevel Level,
    string Message,
    string? MessageTemplate,
    IReadOnlyDictionary<string, object?> State);

/// <summary>
/// In-memory ILoggerProvider (тестовый log-sink) тестовых хостов батча B-20
/// (кейс TS-030: «тестовый log-sink подключён», NFR-004 «суммарное значение
/// счётчика KDF логируется не реже раза в 60 с»): собирает записи ВСЕХ категорий
/// без собственных фильтров. Копия механики зон B-10/B-19/B-21 (чужие зоны
/// недоступны для ссылок — BL-001 BUG-001). Потокобезопасен.
/// </summary>
public sealed class B20LogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<B20LogRecord> _entries = [];
    private bool _disposed;

    /// <summary>Копия собранных записей на момент вызова.</summary>
    public IReadOnlyList<B20LogRecord> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new SinkLogger(this, categoryName);
    }

    void IDisposable.Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void Add(B20LogRecord entry)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class SinkLogger(B20LogSink sink, string category) : ILogger
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
            sink.Add(new B20LogRecord(
                category,
                logLevel,
                message,
                ExtractTemplate(state),
                ExtractState(state)));
        }
    }

    private static string? ExtractTemplate<TState>(TState state) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> list
            ? list.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string
            : null;

    private static IReadOnlyDictionary<string, object?> ExtractState<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs
                .Where(pair => pair.Key != "{OriginalFormat}")
                .ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        }

        return new Dictionary<string, object?>();
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
