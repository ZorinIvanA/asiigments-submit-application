using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Тестовый хост доменных кейсов B-06 волны «ведомость/upsert сдач, свои сдачи,
/// GET работы по id, границы регистрации, студенты» (TS-145..TS-155, TS-198,
/// TS-199, TS-201, TS-203; FR-006/FR-017/FR-020/FR-021/FR-022).
///
/// Окружение — Development с ДЕМО-НАБОРОМ сида (Seed__DemoData=true, FR-004):
/// given кейсов ссылается на сид напрямую — «сид-сдача student01 1.1» (TS-148),
/// «student31 без группы» (TS-152), «3 сид-сдачи student01» (TS-153),
/// «32 студента сида» (TS-203), «запись семестра 1 номера 1» (TS-198).
/// Демо-сид воспроизводит mock/seed.ts: группы ИК-221 (student01..25)/ИК-222
/// (26..30)/ИК-223 (пустая), student31/32 без группы, работы семестра 1 №1–20
/// и семестра 2 №1–3, сид-сдачи student01×(1.1,1.2,1.3) и student02×1.1.
///
/// Бизнес-время — FakeTimeProvider, закреплённый на 2026-09-20 10:00 UTC — ПОСЛЕ
/// фиксированной метки сид-сдач (SEED_SUBMISSION_UPDATED_AT = 2026-09-11 12:00 UTC):
/// then TS-148 «updatedAt обновлён (новее прежнего)» выполняется детерминированно,
/// независимо от системных часов машины прогона (ADR-002/ADR-010: единственный
/// источник бизнес-времени переопределяется в композиция-корне). Время тестами
/// не сдвигается — TTL минтованных access-JWT и окна лимитеров не истекают.
///
/// Сессии — МИНТ access-JWT через ITokenService хоста (ADR-015); POST /auth/login
/// не используется. Isolation: каждый тестовый класс со своей IClassFixture-фикстурой
/// получает свежий экземпляр приложения — собственное хранилище и счётчики лимитеров,
/// мутации кейсов (upsert дат TS-147/148/151) не пересекаются между классами.
///
/// Механика: при минимальном хостинге (WebApplicationBuilder) значения UseSetting
/// попадают в аргументы точки входа до запуска Program, поэтому ключи передаются
/// с разделителем ':' ('__' иерархию создаёт только провайдер переменных окружения).
/// </summary>
public sealed class B06SubmissionsWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "b06-submissions-test-jwt-signing-key-0123456789abcdef";

    public const string TestTeacherPassword = "b06-submissions-teacher-password1!";

    /// <summary>
    /// Закреплённый момент бизнес-времени хоста: позже метки сид-сдач
    /// (2026-09-11 12:00 UTC) — «updatedAt новее прежнего» детерминировано.
    /// </summary>
    private static readonly DateTimeOffset PinnedNow = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    public B06SubmissionsWebAppFactory() => Time.SetUtcNow(PinnedNow);

    /// <summary>Управляемое бизнес-время тестового хоста (ADR-010).</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        // См. B06WebAppFactory: вотчеры перечитывания конфигурации не нужны —
        // пер-пользовательский лимит inotify в среде прогона ненадёжен.
        builder.UseSetting("hostBuilder:reloadConfigOnChange", "false");

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), TestTeacherPassword);

        // Демо-набор включён ЯВНО: given кейсов волны ссылается на сид-данные (FR-004).
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "true");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
