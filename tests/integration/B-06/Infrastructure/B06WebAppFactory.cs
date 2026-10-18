using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Тестовый хост батча B-06 (зона tests/integration/B-06): собственная копия механики
/// фабрики (фабрики зон B-01..B-07 и src/api/LabsApp.Tests — internal и чужие зоны;
/// изоляция по образцу зон B-01/B-02, BL-001 BUG-001). Окружение — Development
/// (teacher-сид FR-006); фиксированные Auth__JwtKey и Seed__TeacherPassword (ADR-010);
/// Seed__DemoData=false ЯВНО — кейсы батча не зависят от демо-набора (AR-011/NFR-011),
/// «занятые» пользователи создаются DI-сидом (<see cref="TestSessions"/>) или самим
/// сидом преподавателя. Content root — выходной каталог тестов.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// фабрики попадают в аргументы точки входа до запуска Program, поэтому ключи
/// передаются с разделителем ':' ('__' иерархию создаёт только провайдер переменных
/// окружения).
///
/// Кейсы FR-012 задают конкретный RemoteIpAddress («RemoteIpAddress=10.0.0.9» —
/// лимит регистраций 5/час на IP, FR-080): TestServer даёт всем запросам один и тот
/// же RemoteIpAddress, поэтому фикстура регистрирует тестовый IStartupFilter с
/// middleware, подставляющим context.Connection.RemoteIpAddress из заголовка
/// X-Test-Remote-Ip. IStartupFilter оборачивает весь конвейер приложения, поэтому
/// подстановка выполняется до всех проверок приложения (резолвер IP IF-006 читает
/// только Connection.RemoteIpAddress); запросы без заголовка остаются без изменений.
///
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — пустые счётчики лимитеров и пустое
/// хранилище (кроме сид-преподавателя), поэтому выбранные кейсами логины/email
/// свободны ровно так, как сказано в given.
/// </summary>
public sealed class B06WebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b06-integration-test-jwt-signing-key-0123456789abcdef";

    public const string TestTeacherPassword = "b06-test-teacher-password1!";

    /// <summary>Заголовок тестовой подмены RemoteIpAddress соединения (кейсы FR-012).</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // Окружение прогона не предоставляет inotify-инстансы надёжно (пер-пользовательский
        // лимит 128 исчерпывается параллельными батчами): файловые вотчеры перечитывания
        // конфигурации тестовому хосту не нужны — Options читаются при старте
        // (ValidateOnStart), проверяемые HTTP-контракты от hot-reload не зависят.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // AR-011/NFR-011: кейсы батча опираются только на сид-преподавателя (FR-006,
        // идемпотентный) и собственных DI-сидируемых пользователей — демо-набор отключён.
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");

        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>());
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");

    /// <summary>
    /// Тестовая подмена RemoteIpAddress соединения (кейсы FR-012: «RemoteIpAddress=X»):
    /// эмулирует запросы с разных IP без сетевого стека (механика зоны B-03).
    /// </summary>
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    var rawIp = context.Request.Headers[B06WebAppFactory.RemoteIpHeader].ToString();
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
