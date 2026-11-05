using Microsoft.Extensions.Logging;

namespace LabsApp.Observability;

/// <summary>
/// Реализация IEmailSender для сред ≠ Development (IF-005): письмо не доставляется
/// и не журналируется — no-op с ОДНИМ warning без адресата и содержимого
/// (NFR-006: код восстановления вне «EmailDev» не появляется вовсе, а категория
/// «EmailDev» существует только в Development). Категория warning —
/// «Hosting.Configuration» (реестр категорий v2.2: warnings конфигурации —
/// доставка писем предусмотрена только dev-окружением). Регистрируется в
/// композиция-корне во всех окружениях, кроме Development.
/// </summary>
public sealed class ProductionEmailSender : IEmailSender
{
    /// <summary>Категория warning о недоступности доставки (реестр категорий v2.2).</summary>
    public const string LogCategory = "Hosting.Configuration";

    private readonly ILogger _logger;

    public ProductionEmailSender(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(LogCategory);
    }

    /// <summary>
    /// No-op (IF-005): ровно один warning без адресата, темы и текста письма —
    /// ни содержимое, ни адресация в журнал не попадают (NFR-006).
    /// </summary>
    public Task SendAsync(string toEmail, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(toEmail);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        // Состав записи нарочно минимальный: ни адресата, ни содержимого —
        // вне Development код восстановления в журнал не попадает (NFR-006).
        _logger.LogWarning(
            "Доставка email не предусмотрена вне Development: письмо не отправлено и не журналируется (dev-заглушка IEmailSender)");
        return Task.CompletedTask;
    }
}
