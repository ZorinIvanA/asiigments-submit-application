using System.Runtime.CompilerServices;

namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>
/// Окружение тестового процесса батча B-22 (детерминизм харнеса).
///
/// Каждый тестовый хост WebApplicationFactory строит конфигурацию с файловыми
/// watcher'ами (PhysicalFileProvider). На хостах прогона с лимитом
/// fs.inotify.max_user_instances=128 (занято рабочими процессами IDE/браузера и
/// параллельными тестовыми зонами) создание watcher'а падает IOException
/// «inotify instances has been reached» ещё на старте хоста. Переключение .NET
/// на режим опроса (DOTNET_USE_POLLING_FILE_WATCHER=true) устраняет зависимость
/// харнеса от inotify, никак не влияя на проверяемое кейсами поведение
/// (наблюдение за изменением файлов конфигурации — не предмет
/// NFR-001/002/004/005/006/008). Для дочерних процессов ворот TS-185
/// (`dotnet build`/`dotnet test`) переменная передаётся явно
/// (<see cref="B22ProcessRunner.ApplyCommonEnvironment"/>).
/// </summary>
internal static class B22TestProcessEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
