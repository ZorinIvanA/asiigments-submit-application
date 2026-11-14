using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-055 «Токены: Development с пустым ключом — эпизодический ключ и
/// предупреждение» (happy_path, FR-008, P2).
///
/// given: ASPNETCORE_ENVIRONMENT=Development; Auth__JwtKey не задан;
///        тестовый log-sink подключён.
/// when:  старт приложения; GET /health.
/// then:  приложение стартует, /health — 200; в логе есть предупреждение о
///        временном случайном ключе (FR-008: «в Development — эпизодический
///        случайный ключ и предупреждение в лог»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// (Ts052_DevelopmentEphemeralJwtKey) не изменялся.
/// </summary>
public sealed class Ts055_DevelopmentEphemeralKeyHealthTests
{
    [Fact]
    public async Task DevelopmentHostWithoutJwtKey_Starts_Health200_AndLogsEphemeralKeyWarning()
    {
        // given: Development; Auth__JwtKey не задан; log-sink подключён (фикстура).
        using var factory = new B09AuthDevNoJwtKeyFactory();
        using var client = B09AuthSupport.CreateClient(factory);

        // when: старт приложения (хост построен — CreateClient вернул клиента);
        // GET /health.
        using var health = await client.GetAsync(B09AuthHttp.HealthPath);

        // then: приложение стартует, /health — 200 {"status":"ok"}.
        var body = await B09Assertions.ParseObjectAsync(health, HttpStatusCode.OK, "GET /health (TS-055)");
        Assert.Equal("ok", body.GetProperty("status").GetString());

        // then: в логе есть предупреждение о временном случайном ключе —
        // Warning категории «Hosting.Configuration»: названа переменная и
        // эпизодический случайный ключ.
        var warnings = factory.LogSink
            .OfCategory(B09AuthSupport.HostingConfigurationCategory)
            .Where(record => record.Level == LogLevel.Warning)
            .ToList();
        Assert.Contains(
            warnings,
            warning =>
            {
                var text = warning.Serialize();
                return text.Contains("Auth__JwtKey", StringComparison.Ordinal)
                    && text.Contains("эпизодическим случайным ключом", StringComparison.Ordinal);
            });
    }
}
