using System.Diagnostics;

namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Запуск dotnet CLI как внешнего процесса (ворота TS-149/NFR-002: dotnet build,
/// dotnet test) поверх <see cref="ProcessRunner"/> зоны (полный перехват вывода,
/// жёсткий тайм-аут). Самостоятельная настройка окружения дочерних процессов:
/// на хосте прогона нет runtime net8.0 (только 9.x/10.x), поэтому dotnet-команды
/// над src/api (net8.0) нуждаются в DOTNET_ROLL_FORWARD=LatestMajor; телеметрия
/// отключена; файловый вотчер — polling (независимость от лимита inotify хоста).
/// Зона тестов (tests/integration/B-19) не входит в src/api/LabsApp.sln —
/// рекурсии прогона нет.
/// </summary>
public static class DotNetCli
{
    /// <summary>Запустить «dotnet &lt;аргументы&gt;» в заданной рабочей директории.</summary>
    public static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotNetMuxer(),
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyCommonEnvironment(startInfo.Environment);
        return await ProcessRunner.RunAsync(startInfo, timeoutMilliseconds);
    }

    /// <summary>Базовые переменные окружения детерминизма для дочерних dotnet-процессов.</summary>
    public static void ApplyCommonEnvironment(IDictionary<string, string?> environment)
    {
        environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
        // Утечка окружения SDK родителя: тест-хост зоны запущен SDK 10, который
        // прописывает MSBUILD_EXE_PATH/MSBuildSDKsPath/DOTNET_HOST_PATH в процесс.
        // Дочерний dotnet с этими переменными берёт ЗАДАЧИ SDK 10 при рантайме 8
        // (или наоборот) — MSB4062 «не удалось загрузить задачу». Переменные
        // снимаются: дочерний muxer резолвит SDK по собственному корню.
        environment["MSBUILD_EXE_PATH"] = null;
        environment["MSBuildSDKsPath"] = null;
        environment["MSBuildExtensionsPath"] = null;
        environment["DOTNET_HOST_PATH"] = null;
        // MSBuild-узлы переиспользования живут после выхода dotnet build и держат
        // открытыми наследованные трубы вывода — родитель висит на ReadToEnd до
        // тайм-аута (урок зоны B-22): переиспользование узлов в дочерних сборках
        // отключается.
        environment["MSBUILDDISABLENODEREUSE"] = "1";

        // Рантайм net8.0 на хосте прогона есть только в пользовательской установке
        // (~/.dotnet): системный muxer содержит лишь 9.x/10.x, и LatestMajor-прокат
        // net8→net10 ломает WebApplicationFactory-тесты (AspNetCore.App 8→10).
        // Дочерним dotnet-процессам явно задаётся корень установки с 8.x
        // (DOTNET_ROOT_X64 имеет приоритет над DOTNET_ROOT — перекрываем оба).
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            environment["DOTNET_ROOT"] = dotnetRoot;
            environment["DOTNET_ROOT_X64"] = dotnetRoot;
        }
    }

    /// <summary>
    /// Корень установки .NET, содержащей рантайм Microsoft.NETCore.App 8.x:
    /// заданный DOTNET_ROOT, затем ~/.dotnet, системные расположения. null — если
    /// нигде не найден (тогда запуск идёт на механизмах разрешения по умолчанию).
    /// </summary>
    private static string? ResolveDotNetRootWithNet8Runtime()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".dotnet"),
            "/usr/lib/dotnet",
            "/usr/local/dotnet",
        };

        foreach (var root in candidates)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            var coreAppDirectory = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            if (Directory.Exists(coreAppDirectory)
                && Directory.GetDirectories(coreAppDirectory, "8.*", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>
    /// Путь к muxer'у .NET, под которым дочерние dotnet-команды идут ПОЛНОСТЬЮ
    /// на установке с рантаймом 8.x (пользовательская установка ~/.dotnet):
    /// системный muxer (SDK 10, рантаймы 9/10) прокатывает тест-хост net8→net10
    /// через DOTNET_ROLL_FORWARD, и AspNetCore.App 10 даёт иное поведение
    /// эндпойнтов приложения (ложные 500 на зелёном на net8 наборе). Установки
    /// с 8.x нет — системный «dotnet» (поведение прежних прогонов).
    /// </summary>
    private static string ResolveDotNetMuxer()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            return "dotnet";
        }

        var userRoot = Path.Combine(home, ".dotnet");
        var userMuxer = Path.Combine(userRoot, "dotnet");
        if (!File.Exists(userMuxer))
        {
            return "dotnet";
        }

        var coreAppDirectory = Path.Combine(userRoot, "shared", "Microsoft.NETCore.App");
        return Directory.Exists(coreAppDirectory)
            && Directory.GetDirectories(coreAppDirectory, "8.*", SearchOption.TopDirectoryOnly).Length > 0
                ? userMuxer
                : "dotnet";
    }

}
