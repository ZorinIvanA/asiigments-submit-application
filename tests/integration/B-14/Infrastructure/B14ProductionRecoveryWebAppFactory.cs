using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Тестовый хост кейса TS-070 «Код восстановления не попадает в основные логи»,
/// стенд (б) Production (FR-012 AC «Код не попадает в основной лог»; NFR-006):
/// окружение Production с ЯВНО заданными Auth__JwtKey (≥32 байт — AuthOptionsValidator)
/// и НЕстандартным Seed__TeacherPassword (SeedOptionsValidator: обязательно, ≠
/// 'teacher123!', правила §8) — иначе fail-fast валидаторы не дадут хосту стартовать.
///
/// Стенд НЕ наследует B14RecoveryWebAppFactory (та ставит Development в своей
/// ConfigureWebHost — файл не модифицируется, чужие файлы тестов не правятся),
/// а повторяет конфигурацию зоны B14WebAppFactory с заменой окружения:
///  - Seed__DemoData=false — кейсу нужен только DI-сид собственных пользователей;
///  - Auth__Pbkdf2Iterations=1000 (умолчание тестовых фабрик зоны);
///  - B14RecoveryLogSink в конвейере журналирования — проверка «ни одна запись
///    не содержит код» идёт по ВСЕМ записям sink всех категорий;
///  - B14ControllableClock вместо TimeProvider.System (единый источник
///    бизнес-времени, FR-003/ADR-002).
///
/// В Production IEmailSender = ProductionEmailSender (выбор по окружению в
/// композиция-корне, IF-005): код в журнал не попадает вовсе — no-op warning
/// без адресата и содержимого.
/// </summary>
public sealed class B14ProductionRecoveryWebAppFactory : B14WebAppFactory
{
    /// <summary>Log-sink хоста: ВСЕ записи журнала для проверки утечки кода.</summary>
    public B14RecoveryLogSink LogSink { get; } = new();

    /// <summary>Инжектируемые часы хоста (FR-003) — единообразие с зоной.</summary>
    public B14ControllableClock Clock { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Production).</summary>
    public B14ProductionRecoveryWebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Конфигурация зоны B14WebAppFactory повторяется с заменой окружения:
        // base.ConfigureWebHost не вызывается, он фиксирует Development.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");

        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
