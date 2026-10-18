using System.Runtime.CompilerServices;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>
/// Окружение тестового процесса батча B-21 (детерминизм харнеса, ADR-010).
///
/// Каждый тестовый хост WebApplicationFactory строит конфигурацию с файловыми
/// watcher'ами (PhysicalFileProvider). На хостах прогона с лимитом
/// fs.inotify.max_user_instances=128 (занято рабочими процессами IDE/браузера)
/// создание watcher'а падает IOException «inotify instances has been reached»
/// ещё на старте хоста. Переключение .NET на режим опроса
/// (DOTNET_USE_POLLING_FILE_WATCHER=true) устраняет зависимость харнеса от
/// inotify, никак не влияя на проверяемое кейсами поведение (наблюдение за
/// изменением файлов конфигурации — не предмет NFR-001/002/004/005/006/008).
/// Для дочерних процессов ворот TS-185/TS-191 (`dotnet build`/`dotnet test`) переменная
/// передаётся явно.
/// </summary>
internal static class B21TestProcessEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
