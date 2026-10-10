using System.Diagnostics;

namespace LabsApp.IntegrationTests.B21.Infrastructure;

/// <summary>Итог дочернего процесса ворот TS-185/TS-191: код выхода и объединённый вывод.</summary>
public sealed record B21ProcessResult(int ExitCode, string Output)
{
    /// <summary>Хвост вывода для диагностических сообщений assert'ов.</summary>
    public string Tail(int length = 4000)
    {
        var normalized = Output.ReplaceLineEndings(" | ").Trim();
        return normalized.Length <= length ? normalized : "…" + normalized[^length..];
    }
}

/// <summary>
/// Запуск дочерних процессов ворот TS-185/TS-191 (NFR-002: dotnet build /
/// dotnet test / ng build / ng test) с чтением обоих потоков вывода и жёстким тайм-аутом
/// (превышение — остановка всего дерева процесса и TimeoutException).
/// </summary>
public static class B21ProcessRunner
{
    public static async Task<B21ProcessResult> RunAsync(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Не удалось запустить процесс: {startInfo.FileName} {startInfo.Arguments}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
        string stdout;
        string stderr;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            // Выход процесса не гарантирует EOF труб: переиспользуемые MSBuild-узлы
            // переживают дочерний dotnet build/test и вечно держат трубы вывода
            // открытыми (урок зон B-19/B-20/B-22) — слив ограничен тем же
            // тайм-аутом, что и WaitForExitAsync: потеря EOF диагностируется
            // тайм-аутом, а не бесконечным зависанием гейта.
            stdout = await stdoutTask.WaitAsync(timeoutCts.Token);
            stderr = await stderrTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Процесс «{startInfo.FileName} {startInfo.Arguments}» превысил тайм-аут "
                + $"{timeoutMilliseconds / 1000} с и остановлен. "
                + $"stdout (хвост): {TailOf(await DrainWithFallbackAsync(stdoutTask))} "
                + $"stderr (хвост): {TailOf(await DrainWithFallbackAsync(stderrTask))}");
        }

        var output = string.Concat(stdout, Environment.NewLine, stderr);
        return new B21ProcessResult(process.ExitCode, output);
    }

    /// <summary>
    /// Слив потока вывода для диагностики уже остановленного процесса: короткий
    /// резервный тайм-аут — если EOF трубы потерян (переиспользуемые MSBuild-узлы),
    /// диагностика завершается по тайм-ауту, а не зависает.
    /// </summary>
    private static async Task<string> DrainWithFallbackAsync(Task<string> streamTask)
    {
        try
        {
            return await streamTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return "<слив потока не завершился за 5 с — EOF трубы потерян, вывод неполный>";
        }
    }

    private static string TailOf(string text)
    {
        var normalized = text.ReplaceLineEndings(" | ").Trim();
        return normalized.Length <= 2000 ? normalized : "…" + normalized[^2000..];
    }

    /// <summary>Базовые переменные окружения детерминизма для дочерних dotnet/CLI-процессов.</summary>
    public static void ApplyCommonEnvironment(IDictionary<string, string?> environment)
    {
        // На хостах прогона нет runtime net8.0 (только 9.x/10.x) — дочерние
        // dotnet-команды над src/api (net8.0) нуждаются в roll-forward.
        environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        // Дочерние хосты не должны зависеть от лимита inotify машины прогона
        // (см. B21TestProcessEnvironment).
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

        // MSBuild не оставляет переиспользуемые узлы: они живут после выхода
        // dotnet build, наследуют дескрипторы перенаправленного stdout и вечно
        // держат открытыми трубы вывода (конвенция зон B-19/B-20/B-22).
        environment["MSBUILDDISABLENODEREUSE"] = "1";
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

}
