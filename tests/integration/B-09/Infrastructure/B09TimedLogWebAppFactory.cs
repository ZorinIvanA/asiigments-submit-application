using System.Globalization;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Хост зоны B-09 с инжектируемыми часами И log-sink одновременно (кейс TS-180,
/// NFR-004 verification «проверка записи лога при закреплённом тестовом времени»):
/// базовая <see cref="B09WebAppFactory"/> не подключает log-sink, а семейство
/// <see cref="B09AuthWebAppFactory"/> не подменяет TimeProvider — кейсу нужны ОБА
/// шва в одном хосте. Новый файл текущей волны батча (существующая инфраструктура
/// зоны не изменяется; механика — объединение двух существующих фабрик зоны).
///
/// KdfCounter строит 60-секундный таймер лога через TimeProvider.CreateTimer
/// (ADR-002): FakeTimeProvider детерминированно зажигает тик при Advance —
/// записи лога наблюдаются без реального ожидания. Замена TimeProvider в
/// ConfigureServices выполняется после завершения Program, поэтому singleton
/// KdfCounter (создаётся при первом разрешении — сидом/хэшером на старте хоста)
/// получает часы теста (образец — B09TimedWebAppFactory). Auth__Pbkdf2Iterations=1000
/// (FR-027: тесты используют малые итерации KDF); Seed__DemoData=false и
/// log-sink в конвейере журналирования — как у базовых фабрик зоны.
/// </summary>
public sealed class B09TimedLogWebAppFactory : B09WebAppFactory
{
    /// <summary>Малое число итераций KDF теста (FR-027).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Единственный источник бизнес-времени тестового хоста.</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>Log-sink тестового хоста; изоляция — Clear() перед сценарием.</summary>
    public B09LogSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(
            ToColonKey(AuthOptions.Pbkdf2IterationsVariable),
            TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        builder.ConfigureServices(services =>
        {
            // Последняя регистрация TimeProvider выигрывает разрешение из DI:
            // KdfCounter и потребители TTL получают часы теста.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Ключ UseSetting — с разделителем ':' ('__'-иерархию создаёт только провайдер переменных окружения).</summary>
    private static string ToColonKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
