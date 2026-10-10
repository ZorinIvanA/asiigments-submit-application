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
/// Самостоятельная настройка окружения дочерних процессов: на хосте прогона нет
/// runtime net8.0 (только 9.x/10.x), поэтому dotnet-команды над src/api (net8.0)
/// нуждаются в DOTNET_ROLL_FORWARD=LatestMajor; телеметрия отключена; файловый
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

        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
        // Утечка окружения SDK родителя: тест-хост зоны запущен SDK 10, который
        // прописывает MSBUILD_EXE_PATH/MSBuildSDKsPath/DOTNET_HOST_PATH в процесс.
        // Дочерний dotnet с этими переменными берёт ЗАДАЧИ SDK 10 при рантайме 8
        // (или наоборот) — MSB4062 «не удалось загрузить задачу». Переменные
        // снимаются: дочерний muxer резолвит SDK по собственному корню.
        startInfo.Environment["MSBUILD_EXE_PATH"] = null;
        startInfo.Environment["MSBuildSDKsPath"] = null;
        startInfo.Environment["MSBuildExtensionsPath"] = null;
        startInfo.Environment["DOTNET_HOST_PATH"] = null;
        // Рантайм net8.0 на хосте прогона есть только в пользовательской установке
        // (~/.dotnet): системный muxer содержит лишь 9.x/10.x, и LatestMajor-прокат
        // net8→net10 ломает WebApplicationFactory-тесты (AspNetCore.App 8→10).
        // Дочерним dotnet-процессам явно задаётся корень установки с 8.x
        // (DOTNET_ROOT_X64 имеет приоритет над DOTNET_ROOT — перекрываем оба).
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
        }

        return await B17ProcessRunner.RunAsync(startInfo, timeoutMilliseconds);
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
