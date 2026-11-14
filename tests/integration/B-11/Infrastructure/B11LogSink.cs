using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>Одна запись журнала, собранная <see cref="B11LogSink"/>.</summary>
public sealed record B11LogEntry(string Category, LogLevel Level, string Message);

/// <summary>
/// In-memory ILoggerProvider (тестовый log-sink) recovery/reset-кейсов батча B-11
/// (TS-064..TS-066: «тестовый log-sink с категориями», NFR-006): собирает записи
/// ВСЕХ категорий без собственных фильтров — разделение категорий выполняют сами
/// кейсы. Копия механики зон B-05/B-10 (чужие зоны недоступны для ссылок —
/// BL-001 BUG-001). Потокобезопасен.
/// </summary>
public sealed class B11LogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<B11LogEntry> _entries = [];
    private bool _disposed;

    /// <summary>Копия собранных записей на момент вызова.</summary>
    public IReadOnlyList<B11LogEntry> Snapshot()
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

    private void Add(B11LogEntry entry)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class SinkLogger(B11LogSink sink, string category) : ILogger
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
            sink.Add(new B11LogEntry(category, logLevel, message));
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
