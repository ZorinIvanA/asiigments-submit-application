using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Тестовый хост батча B-03 (зона tests/integration/B-03; кейсы TS-031..TS-038 —
/// регистрация FR-006, TS-088..TS-094 — список/создание работ FR-017): собственная
/// фабрика зоны (изоляция тестовых зон, BL-001 BUG-001), окружение Development,
/// Seed__DemoData=false ЯВНО — данные given кейсов наполняются DI-сидом
/// (<see cref="B03DomainSeed"/>, <see cref="B03UserSeed"/>), состав хранилища
/// детерминирован («известен состав пользователей до запроса», given TS-032).
/// Сид-преподаватель создаётся при любом Seed__DemoData, поэтому входной шаг
/// «сессия teacher» (given TS-088..TS-094) исполним (<see cref="B03TeacherSession"/>).
/// Auth__JwtKey задаётся явно; Auth__Pbkdf2Iterations=1000 — умолчание тестовых
/// хостов по FR-027 («малые итерации KDF», given TS-031): ключ безвреден, пока
/// реворк Api.Auth.Core не связал его, и заработает автоматически после приземления.
///
/// При минимальном хостинге (WebApplicationBuilder) значения UseSetting фабрики
/// попадают в аргументы точки входа до запуска Program, поэтому ключи передаются
/// с разделителем ':' (иерархию '__' создаёт только провайдер переменных окружения).
///
/// FR-004: регистрационный лимитер ключуется по IP ('IP', окно 3600 с, лимит 5).
/// TestServer даёт всем запросам один RemoteIpAddress, поэтому фикстура регистрирует
/// тестовый IStartupFilter с middleware, подставляющим context.Connection.RemoteIpAddress
/// из заголовка <see cref="RemoteIpHeader"/> (ADR-006: IP клиента = Connection.RemoteIpAddress;
/// обработка X-Forwarded-For вне области). IStartupFilter оборачивает весь конвейер
/// приложения — подстановка выполняется до всех проверок; запросы без заголовка
/// остаются без изменений. Каждый регистрационный кейс использует свой IP — лимит
/// 5/час не пересекается между кейсами батча и соседними зонами.
/// </summary>
public class B03HostFactory : WebApplicationFactory<Program>
{
    /// <summary>Явный ключ подписи JWT тестового хоста (FR-008, ADR-010).</summary>
    public const string TestJwtKey = "b03-integration-test-jwt-signing-key-0123456789abcdef";

    /// <summary>Заголовок тестовой подмены RemoteIpAddress соединения (см. шапку класса).</summary>
    public const string RemoteIpHeader = "X-B03-Remote-Ip";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);
        // Наблюдатель reload-on-change appsettings.json не нужен тестовому хосту и
        // исчерпывает inotify-лимит машины прогона (по экземпляру на каждый из
        // изолированных хостов IClassFixture) — отключается для стабильности харнеса;
        // на проверяемые HTTP-контракты не влияет.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");
        builder.UseSetting(ConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        builder.UseSetting(ConfigKey(SeedOptions.DemoDataVariable), "false");
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, RemoteIpSubstitutionFilter>());
    }

    /// <summary>'__'-имя переменной окружения → ':'-ключ конфигурации (см. шапку класса).</summary>
    protected static string ConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Тестовая подмена RemoteIpAddress соединения (см. шапку класса): эмулирует
    /// запросы с разных IP без сетевого стека.
    /// </summary>
    private sealed class RemoteIpSubstitutionFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    var rawIp = context.Request.Headers[B03HostFactory.RemoteIpHeader].ToString();
                    if (System.Net.IPAddress.TryParse(rawIp, out var remoteIp))
                    {
                        context.Connection.RemoteIpAddress = remoteIp;
                    }

                    await pipeline();
                });

                next(app);
            };
    }
}
