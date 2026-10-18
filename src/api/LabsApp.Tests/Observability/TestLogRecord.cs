using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Одна запись журнала, собранная TestLogSink. State — ключи структурированного
/// состояния вызова ILogger (служебный ключ шаблона {OriginalFormat} в State не
/// попадает — он выделен в MessageTemplate).
/// </summary>
public sealed record TestLogRecord(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    string? MessageTemplate,
    Exception? Exception,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, object?> State);
