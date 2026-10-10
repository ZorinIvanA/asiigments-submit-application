using LabsApp.Observability;
using Microsoft.AspNetCore.Mvc;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Служебный контроллер ТОЛЬКО для гейта конверта ошибок (T-112, FR-023):
/// эндпойнт, который вызывает инжектированный сервис приложения — исключение
/// выбрасывает ЗАМЕНЁННАЯ В DI реализация <see cref="ISecurityEventLogger"/>
/// (имитация сбоя сервиса, AC FR-023 «500 без деталей»), а не сам контроллер.
/// Подключается фабрикой через ApplicationPart (вся сборка LabsApp.Tests —
/// см. TestWebAppFactory.ConfigureWebHost); доменным не является и в состав
/// LabsApp не входит.
/// </summary>
[ApiController]
public sealed class HostingGateSmokeController(ISecurityEventLogger securityEvents) : ControllerBase
{
    /// <summary>
    /// Деталь исключения бросающей реализации — по её ОТСУТСТВИЮ в ответе
    /// проверяется 500-конверт (FR-023: без имён типов и stack trace).
    /// </summary>
    public const string InjectedFailureDetail = "injected-service-failure-HostingGate";

    [HttpGet("api/v1/host-gate/injected-failure")]
    public IActionResult InjectedFailure()
    {
        securityEvents.LogPasswordChanged();
        return Ok();
    }
}
