using System.Runtime.CompilerServices;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Окружение тестового процесса батча B-12 (копия механики зон B-01/B-10/B-14 —
/// CR-001: чужие инициализаторы в сборки этой зоны не попадают, BL-001 BUG-001).
/// Инициализируется до любого хоста (ModuleInitializer).
///
/// DOTNET_USE_POLLING_FILE_WATCHER=true: тестовые хосты WebApplicationFactory
/// создают PhysicalFileProvider (конфигурация/static-файлы), чей FileSystemWatcher
/// расходует экземпляры inotify (лимит хоста — 128 на пользователя); у зоны B-12
/// 12 тест-классов со своей фабрикой-фикстурой IClassFixture&lt;B12WebAppFactory&gt;
/// (12 короткоживущих хостов на прогон зоны), и при параллельных прогонах батчей
/// лимит исчерпан — построение хоста падает System.IO.IOException «The configured
/// user limit (128) on the number of inotify instances has been reached» ещё на
/// WebApplication.CreateBuilder. Переключение провайдеров на polling-наблюдение
/// (задокументированный механизм .NET) снимает зависимость каркаса тестов от
/// системного лимита: reload по изменению файлов в интеграционных кейсах
/// TS-136..TS-147 не используется (конфигурация хостов задаётся явно через
/// UseSetting, ADR-010/ADR-013).
/// </summary>
internal static class B12TestProcessEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
