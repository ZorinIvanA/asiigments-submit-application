using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Тестовый хост батча B-21 (зона tests/integration/B-21): собственная копия механики
/// фабрики (фабрики зон B-01..B-15 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-09, BL-001 BUG-001). Окружение — Development.
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010); Seed__DemoData=false
/// ЯВНО — кейсы батча не зависят от демо-набора (AR-011/NFR-011), доменные данные
/// задаются DI-сидом (NFR-001: «датасет … готовится прямым доступом к in-memory
/// репозиториям через DI тестового хоста»). Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (TS-189, NFR-006): каждый тестовый хост получает <see cref="B21LogSink"/>
/// (LogSink), собирающий записи всех категорий; доступ — из свойства LogSink.
///
/// Изоляция сценариев (хранилища in-memory — FR-002, ASM-007): каждый тестовый
/// КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр приложения —
/// пустые хранилища (кроме сид-преподавателя FR-004) и чистый sink.
/// </summary>
public class B21WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b21-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b21-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста (записи хоста для TS-031/NFR-004 и TS-189/NFR-006).</summary>
    public B21LogSink LogSink { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B21WebAppFactory()
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

        // Log-sink в конвейере журналирования тестового хоста (категория Api.Request).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Хост бенчмарка TS-196 (NFR-008; REWORK CR-001: актуальный ID кейса —
    /// прежний комментарий «TS-193» относился к доволновой нумерации): тот же
    /// Development-стенд с log-sink и конфигурацией базового класса + заданная
    /// кейсом тестовая поверхность KDF
    /// (given TS-196: Auth__Pbkdf2Iterations=1000).
    /// </summary>
    public sealed class LoginBenchmark : B21WebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        }
    }

    /// <summary>
    /// Хост кейса TS-031 (NFR-004, «инжектируемые часы»; REWORK CR-001:
    /// актуальный ID кейса — прежний комментарий «TS-187» относился к
    /// доволновой нумерации): базовая конфигурация +
    /// подмена TimeProvider в DI на <see cref="FakeTimeProvider"/> через
    /// ConfigureTestServices (регистрации тестов применяются ПОСЛЕ регистраций
    /// Program.cs — подмена выигрывает; ADR-002: 60-секундный лог KDF-счётчика
    /// строится на TimeProvider.CreateTimer и следует за фейковым временем).
    /// </summary>
    public sealed class KdfClock : B21WebAppFactory
    {
        /// <summary>Инжектируемые часы кейса TS-031.</summary>
        public FakeTimeProvider Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(Clock));
        }
    }
}
