using System.Diagnostics;

namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Запуск dotnet CLI как внешнего процесса (ворота TS-149/NFR-002: dotnet build,
/// dotnet test) поверх <see cref="ProcessRunner"/> зоны (полный перехват вывода,
/// жёсткий тайм-аут). Самостоятельная настройка окружения дочерних процессов:
/// dotnet-команды над src/api (net8.0) идут через DOTNET_ROLL_FORWARD=LatestMajor только при отсутствии рантайма 8.x во всех установках хоста (найдена установка с 8.x — DOTNET_ROOT на неё, без проката); телеметрия
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
        environment["DOTNET_ROLL_FORWARD"] = null;
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
        // MSBuild-узлы переиспользования живут после выхода dotnet build и держат
        // открытыми наследованные трубы вывода — родитель висит на ReadToEnd до
        // тайм-аута (урок зоны B-22): переиспользование узлов в дочерних сборках
        // отключается.
        environment["MSBUILDDISABLENODEREUSE"] = "1";

        // Дочерним dotnet-процессам явно задаётся корень установки с рантаймом 8.x,
        // если такой есть (пользовательская установка ~/.dotnet или системный
        // /usr/lib/dotnet): DOTNET_ROOT_X64 имеет приоритет над DOTNET_ROOT —
        // перекрываем оба. Найден корень с 8.x — дети идут на нём без проката:
        // LatestMajor внутри корня, где есть и 9.x/10.x, прыгнул бы на старший
        // мажор (AspNetCore.App 8→10 ломает WebApplicationFactory-тесты).
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            environment["DOTNET_ROOT"] = dotnetRoot;
            environment["DOTNET_ROOT_X64"] = dotnetRoot;

            // MSBuild-переменные родителя (MSBUILD_EXE_PATH/MSBuildSDKsPath/
            // MSBuildExtensionsPath/DOTNET_HOST_PATH) снимаются только если они
            // указывают на установку, ОТЛИЧНУЮ от выбранного корня (задачи чужого
            // SDK — MSB4062). Значения, согласованные с корнем, оставляются:
            // дочерние MSBuild-процессы полагаются на них при резолве SDK, а
            // безусловное снятие «правильных» значений ломает сборку (MSB4236
            // «Could not resolve SDK» — воспроизведено на системной установке
            // /usr/lib/dotnet с SDK 8/9/10).
            RemoveForeignMsBuildEnvironment(environment, dotnetRoot);
        }
        else
        {
            // Рантайма 8.x нет ни в одной установке — единственный способ запустить
            // net8-детей: прокат на старший доступный мажор; сопровождающие
            // MSBuild-переменные родителя снимаются (прежнее поведение —
            // тест-хост зоны и дети разъезжаются по SDK/рантаймам).
            environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
            environment["MSBUILD_EXE_PATH"] = null;
            environment["MSBuildSDKsPath"] = null;
            environment["MSBuildExtensionsPath"] = null;
            environment["DOTNET_HOST_PATH"] = null;
        }
    }

    /// <summary>
    /// Снятие MSBuild-переменных окружения, указывающих на установку .NET,
    /// отличную от <paramref name="dotnetRoot"/>: задачи чужого SDK ломают
    /// дочернюю сборку (MSB4062). Согласованные с корнем значения не трогаются.
    /// </summary>
    private static void RemoveForeignMsBuildEnvironment(IDictionary<string, string?> environment, string dotnetRoot)
    {
        foreach (var name in new[] { "MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildExtensionsPath", "DOTNET_HOST_PATH" })
        {
            if (environment.TryGetValue(name, out var value)
                && !string.IsNullOrEmpty(value)
                && !value.StartsWith(dotnetRoot, StringComparison.Ordinal))
            {
                environment[name] = null;
            }
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
