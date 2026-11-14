using LabsApp.Observability;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>Факт вызова IEmailSender.SendAsync: адресат, тема и текст письма.</summary>
public sealed record B15SentEmail(string ToEmail, string Subject, string Body);

/// <summary>
/// Счётная обёртка IEmailSender (кейс TS-190): единственный способ узнать ТОЧНОЕ
/// значение кода восстановления в Production, где код по определению NFR-006
/// не появляется в журнале — источник значения для поиска «ни одна запись не
/// содержит код». Делегирует СРЕДОСПЕЦИФИЧНОЙ реализации, которую выбирает
/// композиция-корень (Development → DevEmailSender, остальные →
/// ProductionEmailSender), поэтому поведение журналирования письма не меняется —
/// меняется только наблюдаемость вызова для теста. Почта — внешняя система;
/// стаб/шпион только внешней системы разрешён принципами тестирования.
/// </summary>
public sealed class B15EmailSpy : IEmailSender
{
    private readonly object _gate = new();
    private readonly List<B15SentEmail> _sentEmails = [];
    private IEmailSender? _inner;

    /// <summary>Снимок отправленных писем (адресат + текст) на момент чтения.</summary>
    public IReadOnlyList<B15SentEmail> SentEmails
    {
        get { lock (_gate) return _sentEmails.ToArray(); }
    }

    /// <summary>
    /// Присоединяет внутреннюю реализацию (вызывается фабрикой при регистрации
    /// обёртки в DI тестового хоста).
    /// </summary>
    public void AttachInner(IEmailSender inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        lock (_gate) _inner = inner;
    }

    Task IEmailSender.SendAsync(string toEmail, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        lock (_gate)
        {
            _sentEmails.Add(new B15SentEmail(toEmail, subject, body));
        }

        return Inner().SendAsync(toEmail, subject, body);
    }

    private IEmailSender Inner() =>
        _inner ?? throw new InvalidOperationException(
            "B15EmailSpy: внутренняя реализация IEmailSender не присоединена " +
            "(дефект тестовой инфраструктуры зоны B-15).");
}
