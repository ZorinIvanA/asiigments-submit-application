using System.Diagnostics;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Результат дочернего процесса: код выхода и полные потоки вывода
/// (диагностика ассертов ворот TS-146 строится по фактическому выводу).
/// </summary>
public sealed record B17ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;

    /// <summary>
    /// Хвост комбинированного вывода для диагностических сообщений ассертов
    /// (полный вывод dotnet test достигает сотен килобайт).
    /// </summary>
    public string OutputTail(int maxCharacters = 4000)
    {
        var output = CombinedOutput;
        return output.Length <= maxCharacters ? output : "…" + output[^maxCharacters..];
    }
}

/// <summary>
/// Запуск внешних процессов с полным перехватом вывода и жёстким тайм-аутом
/// (ворота TS-146 запускают дочерний dotnet test над зоной unit-тестов
/// бэкенда). Детерминирован: аргументы — списком без shell-интерполяции;
/// при тайм-ауте снимается всё дерево процессов.
/// </summary>
public static class B17ProcessRunner
{
    public static async Task<B17ProcessResult> RunAsync(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Не удалось запустить процесс: {startInfo.FileName}.");

        using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Процесс «{startInfo.FileName}» не завершился за {timeoutMilliseconds} мс.");
        }

        // Страховка: дочерние процессы dotnet test (тест-хост) наследуют
        // перехваченные потоки и могут держать их открытыми после выхода
        // прямого потомка. Даём выводу короткий льготный период, затем
        // снимаем дерево.
        var drained = Task.WhenAll(standardOutputTask, standardErrorTask);
        if (await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(10))) != drained)
        {
            process.Kill(entireProcessTree: true);
        }

        return new B17ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }
}

/// <summary>
/// Запуск dotnet CLI как дочернего процесса поверх <see cref="B17ProcessRunner"/>.
/// Самостоятельная настройка окружения дочерних процессов: при отсутствии рантайма net8.0 во всех установках хоста dotnet-команды над
/// src/api (net8.0) идут через DOTNET_ROLL_FORWARD=LatestMajor (найдена установка
/// с 8.x — DOTNET_ROOT на неё, без проката); телеметрия отключена; файловый
/// вотчер — polling (независимость от лимита inotify хоста). Тест-проект бэкенда
/// (src/api/LabsApp.Tests) не ссылается на зону батча (tests/integration/B-17) —
/// рекурсии прогона нет.
/// </summary>
public static class B17DotNetCli
{
    /// <summary>Запустить «dotnet &lt;аргументы&gt;» в заданной рабочей директории.</summary>
    public static async Task<B17ProcessResult> RunAsync(
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

        startInfo.Environment["DOTNET_ROLL_FORWARD"] = null;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
        // Дочерним dotnet-процессам явно задаётся корень установки с рантаймом 8.x,
        // если такой есть (пользовательская установка ~/.dotnet или системный
        // /usr/lib/dotnet): DOTNET_ROOT_X64 имеет приоритет над DOTNET_ROOT —
        // перекрываем оба. Найден корень с 8.x — дети идут на нём без проката:
        // LatestMajor внутри корня, где есть и 9.x/10.x, прыгнул бы на старший
        // мажор (AspNetCore.App 8→10 ломает WebApplicationFactory-тесты).
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["DOTNET_ROOT_X64"] = dotnetRoot;

            // MSBuild-переменные родителя (MSBUILD_EXE_PATH/MSBuildSDKsPath/
            // MSBuildExtensionsPath/DOTNET_HOST_PATH) снимаются только если они
            // указывают на установку, ОТЛИЧНУЮ от выбранного корня (задачи чужого
            // SDK — MSB4062). Значения, согласованные с корнем, оставляются:
            // дочерние MSBuild-процессы полагаются на них при резолве SDK, а
            // безусловное снятие «правильных» значений ломает сборку (MSB4236
            // «Could not resolve SDK» — воспроизведено на системной установке
            // /usr/lib/dotnet с SDK 8/9/10).
            RemoveForeignMsBuildEnvironment(startInfo.Environment, dotnetRoot);
        }
        else
        {
            // Рантайма 8.x нет ни в одной установке — единственный способ запустить
            // net8-детей: прокат на старший доступный мажор; сопровождающие
            // MSBuild-переменные родителя снимаются (прежнее поведение —
            // тест-хост зоны и дети разъезжаются по SDK/рантаймам).
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
            startInfo.Environment["MSBUILD_EXE_PATH"] = null;
            startInfo.Environment["MSBuildSDKsPath"] = null;
            startInfo.Environment["MSBuildExtensionsPath"] = null;
            startInfo.Environment["DOTNET_HOST_PATH"] = null;
        }

        return await B17ProcessRunner.RunAsync(startInfo, timeoutMilliseconds);
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
