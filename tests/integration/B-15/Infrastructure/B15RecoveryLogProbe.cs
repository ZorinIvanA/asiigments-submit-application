using System.Text.RegularExpressions;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Извлечение живого кода восстановления из журнала тестового хоста — given кейсов
/// TS-083/TS-084/TS-085/TS-207 «живой код … извлечён из [DEV-EMAIL]-записи
/// тестового sink» (IF-005/ADR-012: в Development код восстановления появляется
/// в журнале ТОЛЬКО в категории EmailDev с маркером [DEV-EMAIL], NFR-006).
/// Ищет последнюю [DEV-EMAIL]-запись для указанного адресата в
/// <see cref="B15LogSink"/> зоны и извлекает из неё ровно 6-значный код
/// (IF-003: код — 6 ASCII-цифр). Отсутствие записи/кода — ошибка предусловия
/// кейса, а не молчаливый пропуск.
/// </summary>
public static class B15RecoveryLogProbe
{
    /// <summary>Ровно 6 цифр, не являющихся частью более длинной цифровой последовательности.</summary>
    private static readonly Regex SixDigitCode = new(
        @"(?<!\d)\d{6}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// given «живой код, извлечённый из [DEV-EMAIL]-записи»: последняя запись
    /// с маркером [DEV-EMAIL] для указанного адресата; из её текста извлекается
    /// 6-значный код восстановления.
    /// </summary>
    public static string GetLastRecoveryCodeForEmail(B15LogSink sink, string email)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var snapshot = sink.Snapshot();
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
        var match = SixDigitCode.Match(message);
        Assert.True(
            match.Success,
            $"Предусловие кейса: в [DEV-EMAIL]-записи для «{email}» не найдено 6-значного кода: «{message}».");
        return match.Value;
    }
}
