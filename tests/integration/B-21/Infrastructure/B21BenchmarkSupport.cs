using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Инспекция итоговых записей KDF-счётчика в log-sink (NFR-004): записи
/// опознаются по устойчивому маркеру сообщения («операций KDF»), имени метрики
/// auth_kdf_operations_total (сообщение/шаблон/структурированное состояние)
/// или категории логгера «LabsApp.Auth.KdfCounter». Суммарное значение —
/// тестовый шов IKdfCounter.Snapshot() (контракт заморожен ADR-031).
/// </summary>
internal static class B21KdfCounterLog
{
    /// <summary>Устойчивый фрагмент итоговой записи (KdfCounter, Information).</summary>
    public const string RecordMarker = "операций KDF";

    /// <summary>Имя метрики KDF-операций в записи (KdfCounter.MetricName, IF-016).</summary>
    public const string MetricName = "auth_kdf_operations_total";

    /// <summary>Итоговые записи лога счётчика из снимка log-sink.</summary>
    public static List<B21LogRecord> TotalRecords(IReadOnlyList<B21LogRecord> records) =>
        records.Where(IsTotalRecord).ToList();

    public static bool IsTotalRecord(B21LogRecord record)
    {
        if (record.Message.Contains(RecordMarker, StringComparison.Ordinal)
            || record.Message.Contains(MetricName, StringComparison.Ordinal)
            || (record.MessageTemplate?.Contains(MetricName, StringComparison.Ordinal) ?? false))
        {
            return true;
        }

        if (record.Category.Contains("KdfCounter", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return record.State.Keys.Any(key => key.Contains(MetricName, StringComparison.Ordinal));
    }

    /// <summary>Суммарное значение счётчика — тестовый шов IKdfCounter.Snapshot() (NFR-004).</summary>
    public static long Total(B21WebAppFactory.KdfClock factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot().Values.Sum();

    /// <summary>Компактное описание записей для диагностических сообщений assert'ов.</summary>
    public static string Describe(IReadOnlyList<B21LogRecord> records) =>
        records.Count == 0
            ? "<нет записей>"
            : string.Join(" | ", records.Take(20)
                .Select(record => $"[{record.Category}] {record.Message}"));
}
