using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Живой стек сценария TS-144 «Клиент: dev-прокси ведёт /api и /health на
/// Kestrel» (канонический файл зоны B-19 Ts180_ProxyDevServerTests.cs и его
/// фикстура DevStackFixture переименованы на префикс Ts144/B20 при переиздании
/// реестра): Kestrel — запускается собранный apphost проекта src/api/LabsApp
/// (эквивалент `dotnet run --no-build`, ASPNETCORE_ENVIRONMENT=Development
/// с демо-сидом: SeedOptions.DefaultTeacherLogin/Password = teacher/teacher123!),
/// http://localhost:5080 — порт зафиксирован FR-026 п.5 и proxy.conf.json;
/// dev-сервер — ng serve с proxy.conf.json (serve-таргет angular.json уже
/// содержит proxyConfig) на изолированном порту батча B-20 — 4220 (порт кейсом
/// не фиксирован; «через адрес dev-сервера ng»; порт 4219 закреплён за зоной
/// B-19 — изоляция параллельных батчей). Инициализация: сборка бэкенда (через
/// <see cref="DotNetCli"/> — межпроцессный замок <see cref="SrcApiBuildLock"/>
/// над obj/bin src/api) → ожидание GET /health на 5080 → захват ng-замка
/// <see cref="NgCli.NgProcessLockFilePath"/> (весь срок жизни фикстуры: ng serve
/// долгоживущ и конкурирует за .angular-кэш/dist с ng build/test других
/// коллекций) → запуск ng serve → ожидание GET /health через прокси. Вывод
/// процессов каптурируется и попадает в диагностические сообщения при тайм-ауте
/// готовности.
/// </summary>
public sealed class B20DevStackFixture : IAsyncLifetime
{
    /// <summary>Порт Kestrel — зафиксирован FR-026 п.5 / proxy.conf.json.</summary>
    public const int KestrelPort = 5080;

    /// <summary>Порт dev-сервера ng — изолированный порт батча B-20.</summary>
    public const int DevServerPort = 4220;

    private static readonly TimeSpan BackendBuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BackendReadyTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DevServerReadyTimeout = TimeSpan.FromMinutes(6);

    private Process? _backendProcess;
    private Process? _devServerProcess;
    private IDisposable? _ngProcessLock;
    private readonly StringBuilder _backendOutput = new();
    private readonly StringBuilder _devServerOutput = new();

    public string BackendUrl => $"http://localhost:{KestrelPort}";

    public string DevServerUrl => $"http://localhost:{DevServerPort}";

    public async Task InitializeAsync()
    {
        EnsurePortsAreFree();

        // given: Kestrel запущен (Development с сидом).
        BuildBackend();
        StartBackend();
        await WaitUntilHealthyAsync(
            $"{BackendUrl}/health", BackendReadyTimeout, "Kestrel (dotnet run)", _backendOutput);

        // given: ng serve с src/client/proxy.conf.json. Замок ng-контуров
        // удерживается весь срок жизни фикстуры — ng serve долгоживущ.
        _ngProcessLock = RepoRootFileLock.TryAcquire(
            NgCli.NgProcessLockFilePath, BackendBuildTimeout)
            ?? throw new TimeoutException(
                "Не удалось захватить межпроцессный замок ng-контуров «"
                + NgCli.NgProcessLockFilePath + "»: другая тестовая зона/коллекция "
                + "выполняет ng build/test. Дочерний ng serve не запускался.");
        StartDevServer();
        await WaitUntilHealthyAsync(
            $"{DevServerUrl}/health", DevServerReadyTimeout, "dev-сервер ng serve", _devServerOutput);
    }

    public async Task DisposeAsync()
    {
        foreach (var process in new[] { _devServerProcess, _backendProcess })
        {
            if (process is null)
            {
                continue;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершился — чистить нечего.
            }

            process.Dispose();
        }

        _ngProcessLock?.Dispose();

        await Task.CompletedTask;
    }

    /// <summary>Хвост каптурированного вывода процесса (диагностика падений).</summary>
    public string BackendOutputTail() => OutputTail(_backendOutput);

    public string DevServerOutputTail() => OutputTail(_devServerOutput);

    private static string OutputTail(StringBuilder output)
    {
        lock (output)
        {
            var text = output.ToString();
            return text.Length <= 3000 ? text : "…" + text[^3000..];
        }
    }

    private static void EnsurePortsAreFree()
    {
        foreach (var (port, owner) in new[] { (KestrelPort, "Kestrel (TS-144)"), (DevServerPort, "ng serve (TS-144)") })
        {
            using var probe = new System.Net.Sockets.TcpClient();
            try
            {
                probe.Connect("127.0.0.1", port);
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Соединение не установилось — порт свободен.
                continue;
            }

            throw new InvalidOperationException(
                $"Порт {port} для {owner} уже занят другим процессом — изоляция прогона нарушена. "
                + "Освободите порт (или остановите параллельный батч) и повторите.");
        }
    }

    private void BuildBackend()
    {
        // Сборка через хелпер зоны (DotNetCli): межпроцессный замок src/api,
        // DOTNET_ROLL_FORWARD и DOTNET_ROOT с рантаймом 8.x уже настроены им.
        var result = DotNetCli.Run(
            ["build", ClientZonePaths.BackendProjectFile, "--nologo", "-v", "minimal"],
            BackendBuildTimeout);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"given не выполнен: dotnet build {ClientZonePaths.BackendProjectFile} завершился с кодом "
                + $"{result.ExitCode} (timedOut={result.TimedOut}).{Environment.NewLine}{result.OutputTail()}");
        }
    }

    private void StartBackend()
    {
        // Эквивалент `dotnet run --no-build --project … --no-launch-profile` на хосте,
        // где muxer по умолчанию не содержит рантайма net8.0: `dotnet run` принудительно
        // подставляет дочернему apphost корень СВОЕЙ установки (DOTNET_ROOT перезаписывается),
        // и приложение net8.0 не находит рантайм. Запускаем собранный apphost напрямую —
        // launchSettings не используется (--no-launch-profile), SDK на этапе запуска не нужен.
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveBuiltAppHost(),
            WorkingDirectory = RepoPaths.RepositoryRoot,
        };
        // Development с сидом (given кейса): среда задаётся явно, URL — env-переменной.
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = BackendUrl;
        // Хост прогона на пределе fs.inotify.max_user_instances (128) из-за
        // параллельных батчей — старт бэкенда падал на FileConfigurationProvider
        // (IOException inotify). Кейс проверяет прокси /api и /health, а не
        // hot-reload конфигов: переводим вотчеры .NET на polling — детерминизм
        // старта не зависит от чужой нагрузки на inotify.
        startInfo.Environment["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";

        // Рантайм net8.0 ищем в известной установке (пользовательской ~/.dotnet или
        // системной), если muxer по умолчанию его не содержит.
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            // Арх-специфичная DOTNET_ROOT_X64 имеет ПРИОРИТЕТ над общей DOTNET_ROOT
            // и уже выставлена muxer'ом SDK в тест-хосте (/usr/lib/dotnet, где нет
            // рантайма 8.x) — перекрываем её той же найденной установкой.
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
        }

        // StartWithOutputCapture читает process.StandardOutput/Error — потоки обязаны
        // быть перенаправлены до старта (как это делает StartDevServer); иначе
        // Process.get_StandardOutput() бросает InvalidOperationException.
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        _backendOutput.Clear();
        _backendProcess = StartWithOutputCapture(startInfo, _backendOutput);
    }

    /// <summary>
    /// Путь к собранному apphost проекта бэкенда (bin/Debug/&lt;tfm&gt;/&lt;AssemblyName&gt;):
    /// читается из csproj, т.к. fixture запускает бэкенд в режиме --no-build.
    /// </summary>
    private static string ResolveBuiltAppHost()
    {
        var projectFile = ClientZonePaths.BackendProjectFile;
        var csproj = File.ReadAllText(projectFile);

        var tfm = Regex.Match(csproj, @"<TargetFramework>([^<]+)</TargetFramework>").Groups[1].Value;
        var assembly = Regex.Match(csproj, @"<AssemblyName>([^<]+)</AssemblyName>").Groups[1].Value;
        if (string.IsNullOrWhiteSpace(assembly))
        {
            assembly = Path.GetFileNameWithoutExtension(projectFile);
        }

        var appHost = Path.Combine(
            Path.GetDirectoryName(projectFile) ?? string.Empty, "bin", "Debug", tfm, assembly);
        if (!File.Exists(appHost))
        {
            throw new InvalidOperationException(
                $"given не выполнен: apphost бэкенда не найден ({appHost}) — выполните dotnet build проекта.");
        }

        return appHost;
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

    private void StartDevServer()
    {
        var startInfo = NgCli.CreateNgStartInfo($"serve --port {DevServerPort}");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        _devServerOutput.Clear();
        _devServerProcess = StartWithOutputCapture(startInfo, _devServerOutput);
    }

    private static Process StartWithOutputCapture(ProcessStartInfo startInfo, StringBuilder target)
    {
        startInfo.UseShellExecute = false;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Не удалось запустить процесс: {startInfo.FileName} {startInfo.Arguments}.");
        _ = PumpAsync(process.StandardOutput, target);
        _ = PumpAsync(process.StandardError, target);
        return process;
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder target)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (target)
            {
                target.AppendLine(line);
            }
        }
    }

    private static async Task WaitUntilHealthyAsync(string healthUrl, TimeSpan timeout, string owner, StringBuilder output)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        string? lastStatus = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(healthUrl);
                if ((int)response.StatusCode == 200)
                {
                    return;
                }

                lastStatus = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception error) when (error is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                lastStatus = error.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException(
            $"{owner} не ответил 200 на GET {healthUrl} за {timeout.TotalMinutes:0} мин. "
            + $"Последний статус: {lastStatus ?? "нет запросов"}.{Environment.NewLine}"
            + $"Вывод процесса:{Environment.NewLine}{OutputTail(output)}");
    }
}
