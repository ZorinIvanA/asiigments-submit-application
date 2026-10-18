using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Тестовый хост батча B-09 (зона tests/integration/B-09): собственная копия механики
/// фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-07, BL-001 BUG-001). Окружение — Development.
/// Auth__JwtKey и Seed__TeacherPassword зафиксированы (валидация конфигурации);
/// Seed__DemoData=false ЯВНО — кейсы батча TS-011..TS-025 не зависят от демо-набора
/// (пользователи создаются регистрацией через публичный API, TS-020/TS-023/TS-024),
/// а лимитеры TS-011..TS-017 строятся над собственным SlidingWindowLimiter с
/// FakeTimeProvider вне хоста. Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер
/// переменных окружения).
///
/// Изоляция сценариев (хранилище и лимитеры in-memory — FR-002/FR-024): каждый
/// тестовый КЛАСС со своей IClassFixture-фикстурой получает свежий экземпляр
/// приложения — пустые хранилища (кроме сид-преподавателя), пустые счётчики
/// лимитеров, поэтому IP/email кейсов свободны и укладываются в лимиты.
/// </summary>
public class B09WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b09-integration-test-jwt-signing-key-0123456789abcdef";
    public const string TestTeacherPassword = "b09-test-teacher-password1!";

    /// <summary>Заголовок тестовой подмены RemoteIpAddress соединения (TS-019/TS-020/TS-023/TS-024).</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B09WebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Окружение прогона не предоставляет inotify-инстансы надёжно (пер-пользовательский
        // лимит 128 исчерпывается параллельными батчами — воспроизведено на машине прогона:
        // построение хоста падает IOException в FileSystemWatcher.StartRaisingEvents).
        // Файловые вотчеры перечитывания конфигурации тестовому хосту не нужны: проверяемые
        // контракты от hot-reload не зависят (Pbkdf2PasswordHasher читает IOptions<AuthOptions>
        // лениво; шов B09KdfSeams работает мутацией опций, а не reload-токеном).
        // Образец — B06WebAppFactory.cs (CR-001).
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Кейсы батча опираются только на собственных пользователей, созданных
        // регистрацией через публичный API: демо-набор отключён ЯВНО — детерминизм
        // независимо от умолчаний окружения и быстрее старт.
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        // Подмена RemoteIpAddress соединения (TS-019: 10.0.0.19; TS-020: 10.0.0.21;
        // TS-023: 10.0.0.23/24; TS-024: 10.0.0.25): TestServer даёт всем запросам
        // один и тот же RemoteIpAddress, а регистровый лимит считается по
        // RemoteIpAddress соединения (FR-004, ключ 'IP').
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>());
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Тестовая подмена RemoteIpAddress соединения (образец — зона B-03): IStartupFilter
    /// оборачивает весь конвейер приложения, подстановка выполняется до всех проверок;
    /// запросы без заголовка (или с неразбираемым значением) остаются без изменений.
    /// </summary>
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    var rawIp = context.Request.Headers[B09WebAppFactory.RemoteIpHeader].ToString();
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
