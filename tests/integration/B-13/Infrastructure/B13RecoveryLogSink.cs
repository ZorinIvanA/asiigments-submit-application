using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>Запись журнала тестового sink (категория, уровень, отформатированный текст).</summary>
public sealed record B13RecoveryLogRecord(string Category, LogLevel Level, string Message);

/// <summary>
/// In-memory ILoggerProvider тестового хоста recovery-кейсов батча B-13
/// (TS-162; собственная копия механики зоны — харнесы чужих зон B-01..B-12 и
/// src/api/LabsApp.Tests недоступны для ссылок, BL-001 BUG-001). Провайдер сам
/// не фильтрует — в sink попадают все записи, пропущенные правилами уровня хоста.
///
/// Назначение — given кейса TS-162 «живой код C, значение известно тесту из
/// записи 'EmailDev'»: канал доставки кода в Development — категория EmailDev
/// с маркером [DEV-EMAIL] (IF-005/ADR-012; NFR-006 — единственное место
/// появления кода восстановления в журнале). Извлечение ищет записи ПО МАРКЕРУ
/// [DEV-EMAIL] и адресу получателя; код — ровно 6 цифр в тексте записи.
/// </summary>
public sealed class B13RecoveryLogSink : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<B13RecoveryLogRecord> _records = [];
    private bool _disposed;

    /// <summary>Копия собранных записей на момент вызова.</summary>
    public IReadOnlyList<B13RecoveryLogRecord> Snapshot()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    /// <summary>
    /// given «живой код, извлечённый из [DEV-EMAIL]-записи»: последняя запись
    /// с маркером [DEV-EMAIL] для указанного адресата; из её текста извлекается
    /// 6-значный код восстановления. Отсутствие записи/кода — ошибка предусловия.
    /// </summary>
    public string GetLastRecoveryCodeForEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        List<B13RecoveryLogRecord> snapshot;
        lock (_gate)
        {
            snapshot = [.. _records];
        }

        var marked = snapshot
            .Where(record => record.Message.Contains("[DEV-EMAIL]", StringComparison.Ordinal))
            .ToList();
        var forEmail = marked
            .Where(record => record.Message.Contains(email, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            forEmail.Count > 0,
            $"Предусловие кейса: [DEV-EMAIL]-запись для «{email}» не найдена в тестовом sink " +
            $"(IF-005). [DEV-EMAIL]-записей всего: {marked.Count}, записей всего: {snapshot.Count}.");

        var message = forEmail[^1].Message;
        var match = Regex.Match(message, @"(?<!\d)\d{6}(?!\d)", RegexOptions.CultureInvariant);
        Assert.True(
            match.Success,
            $"Предусловие кейса: в [DEV-EMAIL]-записи для «{email}» не найдено 6-значного кода: «{message}».");
        return match.Value;
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    void IDisposable.Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void Add(B13RecoveryLogRecord record)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _records.Add(record);
            }
        }
    }

    private sealed class SinkLogger(B13RecoveryLogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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

            var message = formatter is not null
                ? formatter(state, exception)
                : state?.ToString() ?? string.Empty;
            sink.Add(new B13RecoveryLogRecord(category, logLevel, message));
        }
    }
}
