using Microsoft.AspNetCore.Mvc;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Служебный контроллер ТОЛЬКО для интеграционных тестов хостинга (подключается
/// фабрикой через ApplicationPart): минимальная поверхность для проверки привязки,
/// сериализации, нормализации фреймворковых 400 и 500-конверта необработанных
/// исключений (IF-001). Доменным не является и в состав LabsApp не входит.
/// </summary>
[ApiController]
public sealed class HostingSmokeController : ControllerBase
{
    public sealed class BindRequest
    {
        public int Page { get; set; }
    }

    [HttpPost("api/v1/host-smoke/bind")]
    public IActionResult Bind([FromBody] BindRequest request) => Ok(request);

    // Деталь исключения, по отсутствию которой проверяется 500-конверт (FR-023:
    // без имён типов и stack trace в ответе).
    public const string ThrownDetail = "secret-internal-detail-TestingSmoke";

    [HttpGet("api/v1/host-smoke/throw")]
    public IActionResult Throw() => throw new InvalidOperationException(ThrownDetail);
}
