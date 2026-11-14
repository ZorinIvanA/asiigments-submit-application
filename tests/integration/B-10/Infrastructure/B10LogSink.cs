using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>Одна запись журнала, собранная <see cref="B10LogSink"/>.</summary>
public sealed record B10LogEntry(string Category, LogLevel Level, string Message);

/// <summary>
/// In-memory ILoggerProvider (тестовый log-sink) тестовых хостов батча B-10
/// (кейс TS-051: «тестовый log-sink подключён», FR-008 «предупреждение в лог»):
/// собирает записи ВСЕХ категорий без собственных фильтров. Копия механики
/// zones B-05/B-21 (чужие зоны недоступны для ссылок — BL-001 BUG-001).
/// Потокобезопасен.
/// </summary>
public sealed class B10LogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<B10LogEntry> _entries = [];
    private bool _disposed;

    /// <summary>Копия собранных записей на момент вызова.</summary>
    public IReadOnlyList<B10LogEntry> Snapshot()
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

    private void Add(B10LogEntry entry)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class SinkLogger(B10LogSink sink, string category) : ILogger
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
            sink.Add(new B10LogEntry(category, logLevel, message));
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
