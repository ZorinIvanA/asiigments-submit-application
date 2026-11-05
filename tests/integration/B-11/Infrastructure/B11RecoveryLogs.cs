using System.Text.RegularExpressions;
using LabsApp.Auth;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Помощники log-sink для recovery-кейсов батча B-11 (TS-064..TS-066): шаблон и
/// извлечение кода из записей категории 'EmailDev' (IF-005/NFR-006) и проверка
/// «секрет отсутствует во всех прочих записях» (NFR-006). Код опознаётся не по
/// форме, а ВЕРИФИКАЦИЕЙ против хэша хранилища (ITokenService.VerifyRecoveryCode) —
/// случайные 6-цифровые фрагменты прочих записей (идентификаторы, счётчики) не
/// дают ложных срабатываний и не маскируют утечку настоящего кода.
/// </summary>
public static class B11RecoveryLogs
{
    /// <summary>Категория dev-писем (DevEmailSender.LogCategory, IF-005).</summary>
    public const string EmailDevCategory = DevEmailSender.LogCategory;

    /// <summary>Маркер dev-письма (DevEmailSender.DevEmailMarker, ISS-003/SEC-002).</summary>
    public const string DevEmailMarker = DevEmailSender.DevEmailMarker;

    /// <summary>
    /// Шаблон записи dev-письма (DevEmailSender): «[DEV-EMAIL] Письмо (dev-канал):
    /// to=&lt;адресат&gt;; subject=&lt;тема&gt;; body=&lt;текст письма&gt;» — один и тот же
    /// для существующего и несуществующего адресата (IF-005, TS-065).
    /// </summary>
    private static readonly Regex DevEmailTemplate = new(
        @"^\[DEV-EMAIL\] Письмо \(dev-канал\): to=(?<to>[^;\r\n]+); subject=(?<subject>.+?); body=(?<body>.+)$",
        RegexOptions.CultureInvariant);

    /// <summary>Кандидаты-коды: ровно 6 ASCII-цифр, не часть более длинной цифровой серии.</summary>
    private static readonly Regex SixDigitCandidate = new(
        "(?<![0-9])[0-9]{6}(?![0-9])",
        RegexOptions.CultureInvariant);

    /// <summary>Записи категории 'EmailDev' в порядке появления.</summary>
    public static IReadOnlyList<B11LogEntry> DevEmailEntries(B11LogSink sink) =>
        sink.Snapshot().Where(entry => entry.Category == EmailDevCategory).ToArray();

    /// <summary>
    /// then-проверка формата записи 'EmailDev' (IF-005): категория, маркер [DEV-EMAIL],
    /// шаблон «to=…; subject=…; body=…». Возвращает совпадение с группами to/subject/body.
    /// </summary>
    public static Match AssertDevEmailFormat(B11LogEntry entry)
    {
        Assert.Equal(EmailDevCategory, entry.Category);
        var match = DevEmailTemplate.Match(entry.Message);
        Assert.True(
            match.Success,
            $"Запись 'EmailDev' не совпадает с шаблоном dev-письма (маркер [DEV-EMAIL], to/subject/body): «{entry.Message}».");
        return match;
    }

    /// <summary>
    /// Извлекает код из записи 'EmailDev' ВЕРИФИКАЦИЕЙ по хэшу хранилища:
    /// среди 6-цифровых кандидатов записи ровно один подтверждается
    /// ITokenService.VerifyRecoveryCode против CodeHash живого кода.
    /// </summary>
    public static string VerifiedCode(B11LogEntry entry, ITokenService tokens, string storedCodeHash)
    {
        foreach (Match candidate in SixDigitCandidate.Matches(entry.Message))
        {
            if (tokens.VerifyRecoveryCode(candidate.Value, storedCodeHash))
            {
                return candidate.Value;
            }
        }

        Assert.Fail(
            $"В записи 'EmailDev' нет кода, верифицируемого по хэшу хранилища: «{entry.Message}».");
        return string.Empty; // недостижимо: Assert.Fail бросает исключение
    }

    /// <summary>
    /// NFR-006-гейт: ни одна запись журнала (кроме допустимой категории
    /// <paramref name="allowedCategory"/>, если задана) не содержит секрет.
    /// </summary>
    public static void AssertNoEntryContains(B11LogSink sink, string secret, string? allowedCategory = null)
    {
        foreach (var entry in sink.Snapshot())
        {
            if (allowedCategory is not null && entry.Category == allowedCategory)
            {
                continue;
            }

            Assert.True(
                !entry.Message.Contains(secret, StringComparison.Ordinal),
                $"Секрет обнаружен в журнале вне допустимой категории: категория «{entry.Category}», " +
                $"уровень «{entry.Level}», запись «{entry.Message}» (NFR-006).");
        }
    }

    /// <summary>
    /// Все 6-цифровые кандидаты во ВСЕХ записях sink (для гейта Production TS-066:
    /// ни один кандидат не верифицируется как код восстановления).
    /// </summary>
    public static IReadOnlyList<string> AllSixDigitCandidates(B11LogSink sink)
    {
        var candidates = new List<string>();
        foreach (var entry in sink.Snapshot())
        {
            foreach (Match match in SixDigitCandidate.Matches(entry.Message))
            {
                candidates.Add(match.Value);
            }
        }

        return candidates;
    }
}
