using System.Globalization;
using System.Text.RegularExpressions;
using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Общие помощники бенчмарков и гейтов зоны B-21 (REWORK CR-001/CR-002: один
/// экземпляр помощника на зону в Infrastructure вместо приватных копий,
/// размноженных по сценарным классам). Только механика — без знания о
/// конкретных кейсах.
/// </summary>

/// <summary>
/// Статистика латентности Stopwatch-замеров бенчмарков NFR-001/NFR-008:
/// p95 — отсечка ceil(0.95·n) отсортированного массива, медиана — средний
/// элемент, форматирование — инвариантная культура (детерминизм сообщений).
/// </summary>
internal static class B21LatencyStats
{
    /// <summary>p95: отсечка ceil(0.95·n) отсортированных замеров (n ≥ 1).</summary>
    public static double Percentile95(IReadOnlyList<double> samples)
    {
        Assert.True(samples.Count > 0, "Нет ни одного успешного замера латентности.");
        var sorted = Sorted(samples);
        return sorted[Math.Max(0, (int)Math.Ceiling(0.95 * sorted.Length) - 1)];
    }

    /// <summary>Медиана: средний элемент отсортированных замеров (n ≥ 1).</summary>
    public static double Percentile50(IReadOnlyList<double> samples)
    {
        Assert.True(samples.Count > 0, "Нет ни одного успешного замера латентности.");
        var sorted = Sorted(samples);
        return sorted[sorted.Length / 2];
    }

    /// <summary>Миллисекунды с одной десятичной цифрой (инвариантная культура).</summary>
    public static string FormatMs(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);

    private static double[] Sorted(IReadOnlyList<double> samples) =>
        samples.OrderBy(value => value).ToArray();
}

/// <summary>
/// Разбор хранимой строки парольного хэша (формат FR-005:
/// 'pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;'):
/// эффективное число итераций KDF — фиксация предусловия «тестовые параметры
/// KDF» бенчмарков NFR-008 (ADR-007: Verify читает параметры из самого хэша).
/// Разделитель в текущем срезе реализации может отличаться ('$' или '|'),
/// позиция параметра итераций — вторая. Нераспознанный формат даёт -1:
/// предусловие честно падает с диагностикой, а не проходит мимо.
/// </summary>
internal static class B21StoredPasswordHash
{
    public static int Iterations(string storedHash)
    {
        var parts = storedHash.Split('$', '|');
        return parts.Length == 4
            && string.Equals(parts[0], "pbkdf2-sha256", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
                ? iterations
                : -1;
    }
}

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

/// <summary>
/// Опознание строк предупреждений в консоли MSBuild/NuGet для гейта
/// «0 предупреждений» (NFR-002): «…: warning CS1234: …» (в т.ч. NuGet NU1xxx;
/// локализованное «предупреждение» тоже матчится). Строки сводки вида
/// «    0 Warning(s)» предупреждениями НЕ считаются (нет кода).
/// </summary>
internal static class B21BuildGateWarnings
{
    public static readonly Regex Line = new(
        @"\b(warning|предупреждение)\s+[A-Z]+\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Строки предупреждений в выводе дочерней команды (в порядке появления).</summary>
    public static List<string> Collect(string output) =>
        output.Split('\n').Where(line => Line.IsMatch(line)).ToList();
}
