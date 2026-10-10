using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>
/// Тестовый хост батча B-22 (зона tests/integration/B-22): собственная копия механики
/// фабрики (фабрики зон B-01..B-21 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-09/B-21, BL-001 BUG-001). Окружение — Development.
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (ADR-010); Seed__DemoData=false
/// ЯВНО — кейсы батча не зависят от демо-набора, доменные данные задаются DI-сидом
/// (<see cref="B22DomainSeed"/>: given TS-184 — «объёмные данные готовятся в обход
/// HTTP-лимитов — напрямую через шов репозиториев»; создание через
/// POST /auth/register недопустимо: регистрационный лимитер 5/час на IP — FR-004).
/// Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__'-иерархию создаёт только провайдер
/// переменных окружения).
///
/// Log-sink (кейсы TS-187/TS-189): каждый тестовый хост получает
/// <see cref="B22LogSink"/>, собирающий записи всех категорий; доступ — из
/// свойства LogSink.
///
/// Изоляция сценариев (хранилища in-memory — FR-002/FR-024): каждый тестовый
/// КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр приложения —
/// пустые хранилища (кроме сид-преподавателя FR-025) и чистый sink.
/// </summary>
public class B22WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b22-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b22-test-teacher-password1!";

    /// <summary>Log-sink тестового хоста (кейсы TS-187/TS-189 — записи всех категорий).</summary>
    public B22LogSink LogSink { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B22WebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Демо-набор отключён ЯВНО: кейсы батча опираются только на сид-преподавателя
        // и DI-сид (автотесты не зависят от демо-сида).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Log-sink в конвейере журналирования тестового хоста (записи всех категорий).
        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Хост бенчмарка TS-193 (NFR-008): тот же Development-стенд с log-sink и
    /// конфигурацией базового класса + заданная кейсом тестовая поверхность KDF
    /// (given TS-193: «Стенд с Auth__Pbkdf2Iterations=1000»).
    /// </summary>
    public sealed class LoginBenchmark : B22WebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        }
    }

    /// <summary>
    /// Хост кейса TS-187 (NFR-004, «инжектируемые часы»): базовая конфигурация +
    /// подмена TimeProvider в DI на <see cref="FakeTimeProvider"/> через
    /// ConfigureTestServices (регистрации тестов применяются ПОСЛЕ регистраций
    /// Program.cs — подмена выигрывает; ADR-002: 60-секундный лог KDF-счётчика
    /// строится на TimeProvider.CreateTimer и следует за фейковым временем).
    /// </summary>
    public sealed class KdfClock : B22WebAppFactory
    {
        /// <summary>Инжектируемые часы кейса TS-187.</summary>
        public FakeTimeProvider Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(Clock));
        }
    }
}
