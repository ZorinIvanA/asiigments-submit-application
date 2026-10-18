using System.Text;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Одна запись журнала, собранная <see cref="B05LogSink"/>. State — ключи
/// структурированного состояния вызова ILogger (служебный ключ шаблона
/// {OriginalFormat} в State не попадает — он выделен в MessageTemplate).
/// Копия механики B08LogRecord зоны B-08 и TestLogRecord зоны src/api/LabsApp.Tests
/// (чужие зоны недоступны для ссылок; изоляция зон — BL-001 BUG-001).
/// </summary>
public sealed record B05LogRecord(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    string? MessageTemplate,
    Exception? Exception,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, object?> State)
{
    /// <summary>
    /// Сериализованная форма записи для substring-проверок: категория, уровень,
    /// шаблон, отформатированное сообщение и все пары состояния «ключ=значение».
    /// </summary>
    public string Serialize()
    {
        var builder = new StringBuilder();
        builder.Append(Category).Append('|').Append(Level).Append('|');
        builder.Append(MessageTemplate ?? string.Empty).Append('|').Append(Message);
        foreach (var pair in State.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }

    /// <summary>Значение структурированного поля по имени без учёта регистра ключа.</summary>
    public object? StateValue(string key) =>
        State.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// In-memory ILoggerProvider (log-sink) тестовых хостов батча B-05: копия механики
/// B08LogSink зоны B-08 (чужая зона недоступна для ссылок, BL-001 BUG-001).
/// Собирает записи ВСЕХ категорий без собственных фильтров. Роль для кейсов
/// батча — извлечение кода восстановления из категории Email.Dev (TS-035 —
/// «код извлечён из категории лога Email.Dev») и проверка отсутствия
/// Email.Dev-событий (TS-166: «событий Email.Dev не создано»).
/// Изоляция тестов — Clear() перед сценарием.
/// </summary>
public sealed class B05LogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<B05LogRecord> _records = [];
    private bool _disposed;

    /// <summary>Количество собранных записей.</summary>
    public int Count
    {
        get { lock (_gate) return _records.Count; }
    }

    /// <summary>Копия собранных записей на момент вызова; последующие записи и Clear её не меняют.</summary>
    public IReadOnlyList<B05LogRecord> Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    /// <summary>Записи конкретной категории (точное сравнение) на момент вызова.</summary>
    public IReadOnlyList<B05LogRecord> OfCategory(string category) =>
        Snapshot().Where(record => string.Equals(record.Category, category, StringComparison.Ordinal)).ToList();

    /// <summary>Очистка между сценариями (изоляция: тест видит только свои записи).</summary>
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

    private void Add(B05LogRecord record)
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

    private sealed class SinkLogger(B05LogSink sink, string category) : ILogger
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

            sink.Add(new B05LogRecord(
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
