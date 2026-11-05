namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Управляемый процесс приложения (dotnet run) для процессных кейсов батча B-02:
/// TS-136 (guard сид-пароля Production, FR-025) проверяется на уровне процесса —
/// код выхода, вывод, реальное состояние порта. Секретные переменные передаются
/// только через окружение дочернего процесса и не покидают его.
/// </summary>
public sealed class AppProcess : IDisposable
{
    private readonly Process _process;
    private readonly object _outputLock = new();
    private readonly StringBuilder _output = new();

    private AppProcess(Process process, int port)
    {
        _process = process;
        Port = port;
        process.OutputDataReceived += (_, args) => AppendLine(args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public int Port { get; }

    public bool HasExited => _process.HasExited;

    /// <summary>Код выхода; корректен после подтверждённого завершения процесса.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>Суммарный вывод (stdout + stderr) процесса.</summary>
    public string Output
    {
        get
        {
            lock (_outputLock)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>
    /// Запускает «dotnet run --no-launch-profile --project src/api/LabsApp».
    /// Приложение — net8.0: если на хосте есть установка с рантаймом 8.x, дочернему
    /// процессу задаётся DOTNET_ROOT на неё; рантайма 8.x нет ни в одной установке —
    /// DOTNET_ROLL_FORWARD=LatestMajor (прежняя адаптация окружения прогона).
    /// Не влияет на проверяемое поведение кейсов (FR-023/FR-025).
    /// Переменная со значением null удаляется из окружения (гарантия «не задана»).
    /// </summary>
    public static AppProcess Start(string environment, int port, IReadOnlyDictionary<string, string?> variables)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepoPaths.SrcApiDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(RepoPaths.LabsAppProjectPath);
        // Адаптация окружения прогона (как DOTNET_ROOT/ROLL_FORWARD ниже): отключает
        // inotify-watcher провайдеров конфигурации в дочернем приложении. Значения
        // конфигурации читаются как обычно — проверяемое поведение кейса (guard
        // сид-пароля, FR-025) не меняется; на хосте прогона исчерпан
        // пользовательский лимит inotify
        // (fs.inotify.max_user_instances), и приложение без ключа падает на создании
        // FileSystemWatcher до валидации конфигурации (System.IO.IOException),
        // маскируя ожидаемые сообщения о переменных.
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("hostBuilder:reloadConfigOnChange=false");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = null;
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            // Найдена установка с рантаймом 8.x — приложение идёт на ней без проката:
            // LatestMajor прыгнул бы на старший мажор (AspNetCore.App 8→10 меняет
            // поведение эндпойнтов).
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["DOTNET_ROOT_X64"] = dotnetRoot;
        }
        else
        {
            // Рантайма 8.x нет ни в одной установке — единственный способ запустить
            // net8-приложение: прокат на старший доступный мажор.
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        }

        foreach (var (name, value) in variables)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить процесс приложения (dotnet run).");

        return new AppProcess(process, port);
    }

    /// <summary>Ожидание завершения процесса; true — завершился сам, false — тайм-аут.</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(timeoutCts.Token);
            _process.WaitForExit();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
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
    /// Ожидание готовности /health: опрос до HTTP 200. Если процесс завершился до
    /// готовности — исключение с выводом процесса (диагностика).
    /// </summary>
    public async Task<HealthProbe> WaitForHealthAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Приложение завершилось (код {_process.ExitCode}) до начала обслуживания запросов. Вывод:{Environment.NewLine}{Output}");
            }

            var probe = await HttpProbes.ProbeHealthAsync(Port);
            if (probe is not null)
            {
                return probe;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Приложение не начало обслуживать запросы за {timeout.TotalSeconds} с (порт {Port}). Вывод:{Environment.NewLine}{Output}");
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // процесс успел завершиться между проверкой и Kill — состояние не меняет
        }

        _process.Dispose();
    }

    private void AppendLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_outputLock)
        {
            _output.AppendLine(line);
        }
    }
}
