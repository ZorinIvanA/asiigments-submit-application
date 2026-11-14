using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-194 «Токены: Production с Auth__JwtKey короче 32 байт не стартует
/// (граница 31/32)» (boundary, FR-008, P0).
///
/// given: две Production-конфигурации хоста: (а) Auth__JwtKey — строка ровно
///        31 байт (31 ASCII-символ); (б) Auth__JwtKey — строка ровно 32 байта;
///        в обеих Seed__TeacherPassword НЕстандартный (константа фикстуры).
/// when:  попытка старта приложения в каждой конфигурации
///        (WebApplicationFactory); для (б) — GET /health после старта.
/// then:  (а) старт прерван ошибкой конфигурации (хост сообщает ошибку
///        валидации конфигурации), HTTP-запросы не обслуживаются — ключ <32
///        байт нарушает MUST-ограничение FR-008 «ключ Auth__JwtKey ≥32 байт»
///        (HS256 криптографически требует ключ ≥256 бит). (б) Приложение
///        стартует; GET /health — 200.
///
/// ASCII-строки фикстур: длина в байтах = string.Length (31/32). Файл текущей
/// волны батча B-09.
/// </summary>
public sealed class Ts194_ProductionJwtKeyLengthBoundaryTests
{
    [Fact]
    public void ProductionHostWith31ByteJwtKey_StartAbortedWithConfigurationError()
    {
        // given (а): Production; Auth__JwtKey — ровно 31 ASCII-байт;
        // Seed__TeacherPassword НЕстандартный.
        using var factory = new B09ProductionJwtKey31BytesFactory();
        Assert.Equal(31, B09ProductionJwtKey31BytesFactory.JwtKey31Bytes.Length);

        // when: попытка старта приложения (валидация конфигурации — на старте).
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // then: старт прерван ошибкой конфигурации: в цепочке исключений
        // названа переменная Auth__JwtKey (обязана содержать не менее 32
        // символов в Production); HTTP-запросы не обслуживаются.
        var messages = B09AuthSupport.FlattenExceptionMessages(exception);
        Assert.Contains(AuthOptions.JwtKeyVariable, messages, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ProductionHostWith32ByteJwtKey_Starts_AndHealthServes200()
    {
        // given (б): Production; Auth__JwtKey — ровно 32 ASCII-байта;
        // Seed__TeacherPassword НЕстандартный.
        using var factory = new B09ProductionJwtKey32BytesFactory();
        Assert.Equal(32, B09ProductionJwtKey32BytesFactory.JwtKey32Bytes.Length);

        // when: старт приложения; GET /health после старта.
        using var client = factory.CreateClient();
        using var health = await client.GetAsync(B09AuthHttp.HealthPath);

        // then: приложение стартует; GET /health — 200 {"status":"ok"}.
        var body = await B09Assertions.ParseObjectAsync(health, HttpStatusCode.OK, "GET /health (TS-194(б))");
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
