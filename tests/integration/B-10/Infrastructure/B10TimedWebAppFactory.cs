using System.Globalization;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Хост батча B-10 с инжектируемыми часами и подменой RemoteIpAddress — для кейсов
/// login/refresh/logout-плитки TS-039..TS-047 и TS-053..TS-058 (FR-007/FR-009/FR-010).
/// FakeTimeProvider заменяет TimeProvider, зарегистрированный в Program (коллбэк
/// ConfigureServices выполняется после завершения Program и до старта хоста, поэтому
/// сид и инициализация эталонных хэшей исполняются уже по фиктивному времени —
/// образец B09TimedWebAppFactory зоны B-09). Время движется ТОЛЬКО явно (Advance):
/// окно login-лимитера 60 с (FR-004) и TTL refresh 7 суток (IF-003) детерминированы.
///
/// Auth__Pbkdf2Iterations=1000 задаётся ДО старта хоста (FR-027: «тесты используют
/// малые Auth__Pbkdf2Iterations»); Seed__TeacherPassword — умолчание базовой фабрики
/// ('teacher123!' — пароль сид-преподавателя кейсов TS-039..TS-045), Seed__DemoData=false
/// наследуется от B10HostFactory (ADR-010/NFR-011).
///
/// Подмена RemoteIpAddress: TestServer оставляет Connection.RemoteIpAddress=null,
/// а ключ лимитера входа — 'lower(trim(login))|IP' (FR-004/IF-006), поэтому фильтр
/// до всего конвейера приложения подставляет IP из заголовка
/// <see cref="RemoteIpHeader"/> (образец — TestRemoteIpStartupFilter зоны B-09).
/// reloadConfigOnChange=false — пер-пользовательский лимит inotify-инстансов
/// исчерпывается параллельными батчами (образец — B09WebAppFactory зоны B-09, CR-001).
/// </summary>
public sealed class B10TimedWebAppFactory : B10HostFactory
{
    /// <summary>Малое число итераций KDF тестов (FR-027).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Заголовок тестовой подмены RemoteIpAddress соединения (ключ 'login|IP').</summary>
    public const string RemoteIpHeader = "X-B10-Test-Remote-Ip";

    /// <summary>Единственный источник бизнес-времени тестового хоста (FR-003/ADR-002).</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override bool DemoData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Файловые вотчеры перечитывания конфигурации тестовому хосту не нужны
        // (проверяемые контракты от hot-reload не зависят) — см. сводку класса.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
        builder.UseSetting(
            ToColonKey(AuthOptions.Pbkdf2IterationsVariable),
            TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureServices(services =>
        {
            // Последняя регистрация TimeProvider выигрывает разрешение из DI:
            // все потребители (движок лимитера, TTL токенов) получают часы теста.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    /// <summary>Ключ UseSetting — с разделителем ':' ('__'-иерархию создаёт только провайдер переменных окружения).</summary>
    private static string ToColonKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Тестовая подмена RemoteIpAddress соединения (образец — зона B-09): запросы
    /// без заголовка (или с неразбираемым значением) остаются без изменений.
    /// </summary>
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    var rawIp = context.Request.Headers[B10TimedWebAppFactory.RemoteIpHeader].ToString();
                    if (IPAddress.TryParse(rawIp, out var remoteIp))
                    {
                        context.Connection.RemoteIpAddress = remoteIp;
                    }

                    await pipeline();
                });

                next(app);
            };
    }
}
