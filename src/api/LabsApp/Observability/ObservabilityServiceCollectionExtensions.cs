using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Observability;

/// <summary>
/// Регистрации наблюдаемости C-012 (IF-005/IF-016): журнал событий безопасности
/// (категория «Api.Security» — единая с ObservabilityMiddleware, точка вызовов
/// сценариев аутентификации) и заглушка доставки email с выбором реализации по
/// окружению. Вызывается один раз из композиция-корня (Program.cs); окружение —
/// единственный аргумент, больше решение об окружении нигде не принимается.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует ISecurityEventLogger (singleton; зависимости — ILoggerFactory
    /// и IHttpContextAccessor для traceId/subject записи) и IEmailSender
    /// (singleton): в Development — <see cref="DevEmailSender"/> (категория
    /// «EmailDev» с маркером [DEV-EMAIL]), в остальных окружениях —
    /// <see cref="ProductionEmailSender"/> (no-op с одним warning без адресата
    /// и содержимого).
    /// </summary>
    public static IServiceCollection AddSecurityEventLogging(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        // ISecurityEventLogger читает HttpContext текущего запроса (traceId,
        // subject) — доступ через IHttpContextAccessor (идемпотентная регистрация).
        services.AddHttpContextAccessor();
        services.AddSingleton<ISecurityEventLogger, SecurityEventLogger>();

        // IF-005: выбор реализации заглушки по IHostEnvironment — потребители
        // (контроллеры волны аутентификации) зависят только от интерфейса.
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailSender, DevEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, ProductionEmailSender>();
        }

        return services;
    }
}
