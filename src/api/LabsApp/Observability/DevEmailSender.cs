using Microsoft.Extensions.Logging;

namespace LabsApp.Observability;

/// <summary>
/// Development-реализация IEmailSender (IF-005): письма журналируются в категорию
/// «EmailDev» — единственную категорию журнала, где допустим код восстановления
/// (NFR-006/ISS-003/SEC-002). Каждая запись несёт маркер [DEV-EMAIL], адресата
/// и текст письма; формат записи одинаков для существующего и несуществующего
/// email — журнал не раскрывает существование учётной записи. SMTP вне области
/// (OQ-001). Регистрируется в композиция-корне только в Development.
/// </summary>
public sealed class DevEmailSender : IEmailSender
{
    /// <summary>Категория dev-писем (существует только в Development, NFR-006).</summary>
    public const string LogCategory = "EmailDev";

    /// <summary>Маркер dev-письма в каждой записи категории (ISS-003/SEC-002).</summary>
    public const string DevEmailMarker = "[DEV-EMAIL]";

    private readonly ILogger _logger;

    public DevEmailSender(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(LogCategory);
    }

    /// <summary>
    /// Письмо в «EmailDev» (IF-005): маркер [DEV-EMAIL], адресат, тема и текст
    /// письма (включая код восстановления). Единый шаблон записи для любого
    /// адресата — существование учётной записи по формату записи не раскрывается.
    /// </summary>
    public Task SendAsync(string toEmail, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        _logger.LogInformation(
            DevEmailMarker + " Письмо (dev-канал): to={To}; subject={Subject}; body={Body}",
            toEmail,
            subject,
            body);
        return Task.CompletedTask;
    }
}
