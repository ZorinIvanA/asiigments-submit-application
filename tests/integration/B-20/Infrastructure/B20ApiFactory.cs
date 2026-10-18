using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Тестовый хост бэкенда батча B-20 (зона tests/integration/B-20): собственная
/// копия механики фабрики (фабрики зон B-01..B-21 и src/api/LabsApp.Tests —
/// internal и чужие зоны; изоляция по образцу зон B-10/B-19/B-21, BL-001
/// BUG-001). Окружение — Development; Auth__JwtKey и Seed__TeacherPassword
/// зафиксированы; content root — выходной каталог тестов. Демо-набор FR-004
/// отключён ЯВНО (Seed__DemoData=false, AR-011/NFR-011: автотесты не зависят
/// от демо-сида); объёмные данные кейсов — DI-сидом
/// (<see cref="B20DomainSeed"/>). Учётка teacher создаётся сидом хоста при
/// любом Seed__DemoData (SeedRunner).
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (TS-030, NFR-004): каждый хост получает <see cref="B20LogSink"/>
/// (LogSink), собирающий записи всех категорий; доступ — из свойства LogSink.
///
/// Изоляция сценариев (хранилища in-memory — FR-002): каждый тестовый КЛАСС со
/// своей IClassFixture-фикстурой получает свежий экземпляр приложения — пустые
/// хранилища (кроме сид-преподавателя) и чистый sink.
/// </summary>
public class B20ApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b20-api-integration-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b20-api-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста (записи всех категорий — TS-030, NFR-004).</summary>
    public B20LogSink LogSink { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B20ApiFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Демо-набор отключён ЯВНО: кейсы батча опираются только на сид-преподавателя
        // и DI-сид (AR-011/NFR-011: автотесты не зависят от демо-сида).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (TS-030: NFR-004).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Хост кейса TS-030 (NFR-004, «инжектируемые часы»): базовая конфигурация +
    /// подмена TimeProvider в DI на <see cref="FakeTimeProvider"/> через
    /// ConfigureTestServices (регистрации тестов применяются ПОСЛЕ регистраций
    /// Program.cs — подмена выигрывает; ADR-002: 60-секундный лог KDF-счётчика
    /// построен на TimeProvider.CreateTimer и следует за фейковым временем).
    /// Тестовая поверхность KDF понижена (Auth:Pbkdf2Iterations=1000): кейс
    /// проверяет период логирования, а не стоимость дериваций; операция KDF
    /// кейса остаётся реальным компонентом IPasswordHasher.
    /// </summary>
    public sealed class KdfClock : B20ApiFactory
    {
        /// <summary>Инжектируемые часы кейса TS-030; T0 — состояние на момент запуска хоста.</summary>
        public FakeTimeProvider Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(Clock));
        }
    }
}
