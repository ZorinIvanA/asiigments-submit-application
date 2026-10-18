using System.Runtime.CompilerServices;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Окружение тестового процесса батча B-16 (адаптация прогона, не поведение кейсов):
/// DOTNET_USE_POLLING_FILE_WATCHER=true — файловые вотчеры конфигурации тестовых
/// хостов (appsettings.json, JsonConfigurationSource → FileSystemWatcher) работают
/// через polling вместо inotify. На машинах прогона с исчерпанным лимитом
/// fs.inotify.max_user_instances (=128) создание WebApplicationFactory-хоста падает
/// с System.IO.IOException «The configured user limit (128) on the number of inotify
/// instances has been reached» ДО выполнения any given/when/then — что маскирует
/// результат кейса машинным состоянием. Polling-режим снимает зависимость от
/// inotify, детерминирован и влияет только на процесс dotnet test этой зоны
/// (переменная задаётся до построения первого хоста — ModuleInitializer;
/// единый механизм с зоной B-14).
/// </summary>
internal static class B16TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
