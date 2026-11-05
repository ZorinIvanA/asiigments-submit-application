using LabsApp.Observability;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Спай-декоратор IEmailSender (TS-064: «IEmailSender вызван один раз»): считает
/// вызовы и делегирует РЕАЛЬНОЙ dev-заглушке <see cref="DevEmailSender"/>, поэтому
/// запись категории 'EmailDev' с маркером [DEV-EMAIL] появляется в log-sink как
/// в основном прогоне (IF-005: подмена — стаб ТОЛЬКО внешней системы доставки;
/// журнал dev-письма остаётся прод-поведением). Потокобезопасен. Собственная
/// копия механики зоны (спаи чужих зон B-01..B-12 недоступны — BL-001 BUG-001).
/// </summary>
public sealed class B13CountingDevEmailSender : IEmailSender
{
    private readonly DevEmailSender _inner;

    private int _calls;

    public B13CountingDevEmailSender(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _inner = new DevEmailSender(loggerFactory);
    }

    /// <summary>Число вызовов SendAsync с момента старта хоста.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public Task SendAsync(string toEmail, string subject, string body)
    {
        Interlocked.Increment(ref _calls);
        return _inner.SendAsync(toEmail, subject, body);
    }
}

/// <summary>
/// Падающая заглушка IEmailSender (TS-064, отдельный прогон «IEmailSender подменён
/// реализацией, бросающей исключение»): захватывает аргументы вызова (тест узнаёт
/// сгенерированный код для проверки «в логе без секретов») и бросает исключение —
/// канал доставки недоступен. Потокобезопасен.
/// </summary>
public sealed class B13ThrowingEmailSender : IEmailSender
{
    private readonly object _gate = new();
    private readonly List<string[]> _captured = [];

    private int _calls;

    /// <summary>Число вызовов SendAsync (вызов с исключением тоже считается).</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Аргументы вызовов [toEmail, subject, body] до броска исключения.</summary>
    public IReadOnlyList<string[]> Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured.ToArray();
            }
        }
    }

    public Task SendAsync(string toEmail, string subject, string body)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _captured.Add([toEmail, subject, body]);
        }

        throw new InvalidOperationException(
            "TS-064: канал доставки email недоступен (падающая тестовая заглушка IEmailSender).");
    }
}
