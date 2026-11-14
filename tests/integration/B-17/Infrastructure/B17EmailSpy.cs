using System.Text.RegularExpressions;
using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>Факт вызова IEmailSender.SendAsync с кодом восстановления: адресат и код письма.</summary>
public sealed record B17SentRecoveryEmail(string Email, string Code);

/// <summary>
/// Счётная обёртка IEmailSender (кейс TS-068: «IEmailSender вызван один раз»;
/// TS-069/TS-070/TS-190 — источник точного значения кода для поиска по журналу).
/// Перехватывает единственный метод текущей поверхности интерфейса —
/// SendAsync(toEmail, subject, body) (IF-005/ADR-012): записывает адресата и
/// извлекает 6-значный код восстановления (формат D6, ASM-005) из текста письма;
/// письмам без 6-значного кода (например, уведомление о смене email) счёт
/// recovery-вызовов не искажают. Затем вызов делегируется СРЕДОСПЕЦИФИЧНОЙ
/// реализации, которую выбирает композиция-корень (Development → DevEmailSender,
/// остальные → ProductionEmailSender), поэтому поведение журналирования письма
/// не меняется — меняется только наблюдаемость вызова для теста. Почта —
/// внешняя система; стаб/шпион только внешней системы разрешён принципами
/// тестирования.
/// </summary>
public sealed class B17EmailSpy : IEmailSender
{
    // Код восстановления — 6 ASCII-цифр (ведущие нули допустимы, TS-068);
    // точный шаблон письма доменом не зафиксирован, поэтому код — первое
    // вхождение 6 цифр в теле, при отсутствии — в теме.
    private static readonly Regex RecoveryCodePattern = new("[0-9]{6}", RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly List<B17SentRecoveryEmail> _sentRecoveryEmails = [];
    private IEmailSender? _inner;

    /// <summary>Число вызовов SendAsync с кодом восстановления на момент чтения.</summary>
    public int RecoverySendsCount
    {
        get { lock (_gate) return _sentRecoveryEmails.Count; }
    }

    /// <summary>Снимок отправленных писем (адресат + код) на момент чтения.</summary>
    public IReadOnlyList<B17SentRecoveryEmail> SentRecoveryEmails
    {
        get { lock (_gate) return _sentRecoveryEmails.ToArray(); }
    }

    /// <summary>
    /// Присоединяет внутреннюю реализацию (вызывается фабрикой при регистрации
    /// обёртки в DI тестового хоста).
    /// </summary>
    public void AttachInner(IEmailSender inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    Task IEmailSender.SendAsync(string toEmail, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        var match = RecoveryCodePattern.Match(body);
        if (!match.Success)
        {
            match = RecoveryCodePattern.Match(subject);
        }

        if (match.Success)
        {
            lock (_gate)
            {
                _sentRecoveryEmails.Add(new B17SentRecoveryEmail(toEmail, match.Value));
            }
        }

        return Inner().SendAsync(toEmail, subject, body);
    }

    private IEmailSender Inner() =>
        _inner ?? throw new InvalidOperationException(
            "B17EmailSpy: внутренняя реализация IEmailSender не присоединена (дефект тестовой инфраструктуры B-17).");
}
