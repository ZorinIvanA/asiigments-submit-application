using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Сценарные хосты батча B-10 для FR-008-кейсов конфигурации ключа подписи и
/// окружения (TS-050/TS-051/TS-192): механика фабрики — собственная копия
/// (фабрики чужих зон и src/api/LabsApp.Tests недоступны — BL-001 BUG-001),
/// конфигурация — явные UseSetting на ключи с разделителем ':' (при минимальном
/// хостинге значения попадают в аргументы точки входа до запуска Program;
/// '__'-иерархию создаёт только провайдер переменных окружения). Content root —
/// выходной каталог тестов (wwwroot рядом с тестами отсутствует — статика и
/// SPA-fallback не участвуют в auth-кейсах). Log-sink подключается каждому
/// сценарному хосту (кейс TS-051: «тестовый log-sink подключён»).
///
/// Ключ «не задан» выражается ЯВНО пустым значением соответствующей переменной:
/// пустое/пробельное Auth__JwtKey оба потребителя (dev-ветка PostConfigure и
/// Production-валидатор AuthOptionsValidator) трактуют как отсутствие ключа, а
/// явная установка вытесняет возможные унаследованные значения окружения
/// тестового процесса — шаг given «Auth__JwtKey не задан» детерминирован.
/// </summary>
public abstract class B10ScenarioHostFactory : WebApplicationFactory<Program>
{
    /// <summary>Log-sink тестового хоста (записи всех категорий без фильтров).</summary>
    public B10LogSink LogSink { get; } = new();

    /// <summary>Имя окружения ASP.NET Core для хоста кейса.</summary>
    protected abstract string HostEnvironment { get; }

    /// <summary>Явные переменные конфигурации хоста (имена в форме 'Auth__JwtKey').</summary>
    protected abstract IReadOnlyDictionary<string, string?> Settings { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        foreach (var (name, value) in Settings)
        {
            builder.UseSetting(ToConfigKey(name), value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// TS-051: Development; Auth__JwtKey НЕ задан (эпизодический ключ + warning);
    /// сид — учётные данные по умолчанию (шаг when «успешный вход»).
    /// </summary>
    public sealed class DevelopmentNoJwtKey : B10ScenarioHostFactory
    {
        protected override string HostEnvironment => Environments.Development;

        protected override IReadOnlyDictionary<string, string?> Settings =>
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = string.Empty,
                [SeedOptions.TeacherPasswordVariable] = SeedOptions.DefaultTeacherPassword,
                [SeedOptions.DemoDataVariable] = "false",
            };
    }

    /// <summary>
    /// TS-050: Production; Auth__JwtKey НЕ задан; Seed__TeacherPassword —
    /// нестандартный (проходит guard сида, чтобы единственной причиной отказа
    /// старта была валидация ключа подписи).
    /// </summary>
    public sealed class ProductionNoJwtKey : B10ScenarioHostFactory
    {
        /// <summary>Нестандартный сид-пароль (удовлетворяет правилам §8, ≠ умолчанию).</summary>
        public const string OwnTeacherPassword = "Ts050-Teacher-Pass1!";

        protected override string HostEnvironment => Environments.Production;

        protected override IReadOnlyDictionary<string, string?> Settings =>
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = string.Empty,
                [SeedOptions.TeacherPasswordVariable] = OwnTeacherPassword,
                [SeedOptions.DemoDataVariable] = "false",
            };
    }

    /// <summary>
    /// TS-192: Production-стенд с валидными секретами — Auth__JwtKey задан
    /// (≥32 симв.), Seed__TeacherPassword нестандартный (guard FR-006 пройден);
    /// полный auth-поток с разбором Set-Cookie.
    /// </summary>
    public sealed class ProductionWithSecrets : B10ScenarioHostFactory
    {
        /// <summary>Тестовый ключ подписи Production-стенда (≥ AuthOptions.JwtKeyMinLength).</summary>
        public const string TestJwtKey = "b10-production-jwt-signing-key-0123456789abcdef-0123456789abcdef";

        /// <summary>Нестандартный сид-пароль (удовлетворяет правилам §8, ≠ умолчанию).</summary>
        public const string OwnTeacherPassword = "Ts192-Teacher-Pass1!";

        protected override string HostEnvironment => Environments.Production;

        protected override IReadOnlyDictionary<string, string?> Settings =>
            new Dictionary<string, string?>
            {
                [AuthOptions.JwtKeyVariable] = TestJwtKey,
                [SeedOptions.TeacherPasswordVariable] = OwnTeacherPassword,
                [SeedOptions.DemoDataVariable] = "false",
            };
    }
}
