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
    /// DOTNET_ROLL_FORWARD=LatestMajor — адаптация окружения прогона (на хосте нет
    /// runtime .NET 8); не влияет на проверяемое поведение кейсов (FR-023/FR-025).
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
        // Адаптация окружения прогона (как DOTNET_ROLL_FORWARD выше): отключает
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
        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";

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
