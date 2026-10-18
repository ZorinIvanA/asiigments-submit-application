using System.Diagnostics;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Запуск Angular CLI (ng build, ng test — ворота TS-149/NFR-002) как внешнего
/// процесса: node + node_modules/@angular/cli/bin/ng.js, рабочая директория —
/// корень репозитория (где angular.json; проект по умолчанию —
/// «asiigments-submit-application» с sourceRoot src/client). Копия механики
/// зоны B-19 (чужая зона недоступна для ссылок — BL-001 BUG-001).
/// Самостоятельная настройка окружения:
///  - CHROME_BIN проставляется ТОЛЬКО если не задан ни CHROME_BIN, ни CHROME_PATH —
///    karma-chrome-launcher ищет бинарник Chrome по стандартным путям, которых на
///    хосте может не быть (например, доступен только snap-пакет); сама команда
///    ng test при этом остаётся дословно «ng test --watch=false --browsers=ChromeHeadless»;
///  - node разрешается по PATH, затем по типовым расположениям (nvm, /usr/bin).
/// </summary>
public static class NgCli
{
    private static readonly string[] ChromeCandidates =
    [
        "/usr/bin/google-chrome-stable",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-beta",
        "/usr/bin/chromium-browser",
        "/usr/bin/chromium",
        "/snap/bin/chromium",
        "/snap/bin/google-chrome",
    ];

    private static string? _nodePath;

    /// <summary>
    /// Единый зафиксированный путь межпроцессного замка ng-контуров: ng build /
    /// ng test работают от корня репозитория и пишут в общие .angular-кэш и
    /// dist/, поэтому параллельные ng-прогоны разных тестовых зон порождают
    /// ложные падения (порча кэша, наложение записи dist). Все зоны, запускающие
    /// ng, обязаны использовать ЭТОТ же путь (образец —
    /// <see cref="SrcApiBuildLock.LockFilePath"/> для dotnet-контуров; механизм
    /// — <see cref="RepoRootFileLock"/>). Замок сериализует прогон на всё время
    /// жизни дочернего процесса; файл создаётся, но никогда не удаляется.
    /// </summary>
    public static readonly string NgProcessLockFilePath =
        Path.Combine(RepoPaths.RepositoryRoot, ".ng-cli-process.lock");

    /// <summary>
    /// Запустить ng с указанными аргументами (без слова «ng») и дождаться
    /// завершения. Прогон сериализуется межпроцессным замком
    /// <see cref="NgProcessLockFilePath"/> (удерживается всё время жизни
    /// дочернего процесса): конкурирующие ng-запуски других тестовых зон не
    /// пересекаются на .angular-кэше и dist. Если замок не освободился за
    /// таймаут, дочерний процесс НЕ запускается — возвращается диагностический
    /// результат с кодом -1 (образец — <see cref="DotNetCli"/>).
    /// </summary>
    public static async Task<ProcessResult> RunNgAsync(string ngArguments, int timeoutMilliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        var lockHandle = RepoRootFileLock.TryAcquire(NgProcessLockFilePath, timeout);
        if (lockHandle is null)
        {
            return new ProcessResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: "Не удалось захватить межпроцессный замок ng-контуров «"
                    + NgProcessLockFilePath + "» за "
                    + timeout.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " с: другая тестовая зона выполняет ng build/test "
                    + "(дочерний процесс не запускался, конкуренции за .angular-кэш/dist нет).");
        }

        using (lockHandle)
        {
            var startInfo = CreateNgStartInfo(ngArguments);
            return await ProcessRunner.RunAsync(startInfo, timeoutMilliseconds);
        }
    }

    /// <summary>
    /// ProcessStartInfo для запуска ng с указанными аргументами (без слова «ng»).
    /// </summary>
    public static ProcessStartInfo CreateNgStartInfo(string ngArguments)
    {
        if (!File.Exists(NfrGatePaths.NgCliJs))
        {
            throw new InvalidOperationException(
                $"Не найден Angular CLI: {NfrGatePaths.NgCliJs}. Выполните npm ci в корне репозитория.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveNodePath(),
            Arguments = $"\"{NfrGatePaths.NgCliJs}\" {ngArguments}",
            WorkingDirectory = RepoPaths.RepositoryRoot,
        };
        EnsureChromeBin(startInfo.Environment);
        return startInfo;
    }

    private static string ResolveNodePath()
    {
        if (_nodePath is not null)
        {
            return _nodePath;
        }

        var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathEnvironment.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(rawDirectory))
            {
                continue;
            }

            var candidate = Path.Combine(rawDirectory.Trim().Trim('"'), "node");
            if (File.Exists(candidate))
            {
                return _nodePath = candidate;
            }
        }

        // Fallback: типовые расположения (nvm ставит node вне системных путей).
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            var nvmVersions = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvmVersions))
            {
                var latest = Directory.GetDirectories(nvmVersions)
                    .Select(path => (Path: path, Name: Path.GetFileName(path)))
                    .OrderByDescending(item => Version.TryParse(item.Name.TrimStart('v'), out var version)
                        ? version
                        : new Version(0, 0))
                    .Select(item => item.Path)
                    .FirstOrDefault();
                if (latest is not null)
                {
                    var nvmNode = Path.Combine(latest, "bin", "node");
                    if (File.Exists(nvmNode))
                    {
                        return _nodePath = nvmNode;
                    }
                }
            }
        }

        foreach (var candidate in new[] { "/usr/bin/node", "/usr/local/bin/node" })
        {
            if (File.Exists(candidate))
            {
                return _nodePath = candidate;
            }
        }

        throw new InvalidOperationException(
            "Не найден исполняемый файл node (PATH, ~/.nvm, /usr/bin, /usr/local/bin).");
    }

    private static void EnsureChromeBin(IDictionary<string, string?> environment)
    {
        var existing = environment.TryGetValue("CHROME_BIN", out var chromeBin) ? chromeBin : null;
        var chromePath = environment.TryGetValue("CHROME_PATH", out var value) ? value : null;
        if (!string.IsNullOrWhiteSpace(existing) || !string.IsNullOrWhiteSpace(chromePath))
        {
            return;
        }

        var chrome = ChromeCandidates.FirstOrDefault(File.Exists);
        if (chrome is not null)
        {
            environment["CHROME_BIN"] = chrome;
        }
    }
}
