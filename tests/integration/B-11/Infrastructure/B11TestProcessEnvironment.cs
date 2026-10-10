using System.Runtime.CompilerServices;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Окружение тестового процесса батча B-11. Инициализируется до любого хоста
/// (ModuleInitializer).
///
/// DOTNET_USE_POLLING_FILE_WATCHER=true: тестовые хосты WebApplicationFactory
/// создают PhysicalFileProvider (конфигурация/static-файлы), чей FileSystemWatcher
/// расходует экземпляры inotify (лимит хоста — 128 на пользователя); при параллельных
/// прогонах батчей и настольных процессах лимит исчерпан и построение хоста падает с
/// System.IO.IOException «The configured user limit (128) on the number of inotify
/// instances has been reached» ещё на WebApplication.CreateBuilder. Переключение
/// провайдеров на polling-наблюдение (задокументированный механизм .NET) снимает
/// зависимость каркаса тестов от системного лимита: reload по изменению файлов в
/// интеграционных кейсах не используется (конфигурация хостов задаётся явно через
/// UseSetting, ADR-010/ADR-013).
/// </summary>
internal static class B11TestProcessEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
