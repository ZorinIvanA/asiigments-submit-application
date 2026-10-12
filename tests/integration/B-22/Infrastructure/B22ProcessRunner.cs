using System.Diagnostics;

namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>Итог дочернего процесса ворот TS-185: код выхода и объединённый вывод.</summary>
public sealed record B22ProcessResult(int ExitCode, string Output)
{
    /// <summary>Хвост вывода для диагностических сообщений assert'ов.</summary>
    public string Tail(int length = 4000)
    {
        var normalized = Output.ReplaceLineEndings(" | ").Trim();
        return normalized.Length <= length ? normalized : "…" + normalized[^length..];
    }
}

/// <summary>
/// Запуск дочерних процессов ворот TS-185 (NFR-002: dotnet build / dotnet test /
/// ng build / ng test) с чтением обоих потоков вывода и жёстким тайм-аутом
/// (превышение — остановка всего дерева процесса и TimeoutException).
/// </summary>
public static class B22ProcessRunner
{
    public static async Task<B22ProcessResult> RunAsync(ProcessStartInfo startInfo, int timeoutMilliseconds)
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
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Процесс «{startInfo.FileName} {startInfo.Arguments}» превысил тайм-аут "
                + $"{timeoutMilliseconds / 1000} с и остановлен. "
                + $"stdout (хвост): {TailOf(await stdoutTask)} stderr (хвост): {TailOf(await stderrTask)}");
        }

        var output = string.Concat(await stdoutTask, Environment.NewLine, await stderrTask);
        return new B22ProcessResult(process.ExitCode, output);
    }

    private static string TailOf(string text)
    {
        var normalized = text.ReplaceLineEndings(" | ").Trim();
        return normalized.Length <= 2000 ? normalized : "…" + normalized[^2000..];
    }

    /// <summary>Базовые переменные окружения детерминизма для дочерних dotnet/CLI-процессов.</summary>
    public static void ApplyCommonEnvironment(IDictionary<string, string?> environment)
    {
        // Хост без runtime net8.0 не может запустить net8-детей напрямую — тогда
        // (и только тогда) ниже выставляется DOTNET_ROLL_FORWARD=LatestMajor.
        environment["DOTNET_ROLL_FORWARD"] = null;
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        // Дочерние хосты не должны зависеть от лимита inotify машины прогона
        // (см. B22TestProcessEnvironment).
        environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
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
        // MSBuild не оставляет переиспользуемые узлы: они живут после выхода
        // dotnet build, наследуют дескрипторы перенаправленного stdout и вечно
        // держат открытыми трубы вывода.
        environment["MSBUILDDISABLENODEREUSE"] = "1";
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

}
