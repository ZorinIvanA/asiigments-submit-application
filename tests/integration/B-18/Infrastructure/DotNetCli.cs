using System.Diagnostics;

namespace LabsApp.IntegrationTests.B18.Infrastructure;

/// <summary>Результат запуска дочерней команды dotnet CLI.</summary>
/// <param name="ExitCode">Код завершения процесса (после Kill — какой успел установиться; -1, если дочерний процесс не запускался).</param>
/// <param name="TimedOut">true, если процесс не уложился в лимит и был снят целиком либо не был запущен из-за занятого межпроцессного замка.</param>
/// <param name="Output">Собранный stdout+stderr (хвост может быть обрезан при аварийном снятии) либо диагностическое сообщение.</param>
public sealed record DotNetCliResult(int ExitCode, bool TimedOut, string Output)
{
    /// <summary>Успех: процесс завершился сам (не по таймауту) и с кодом 0.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>Последние <paramref name="lines"/> строк вывода — для сообщений об ошибках.</summary>
    public string OutputTail(int lines = 60)
    {
        var allLines = Output.Split(Environment.NewLine);
        return allLines.Length <= lines
            ? Output
            : "...(начало вывода обрезано)..." + Environment.NewLine
                + string.Join(Environment.NewLine, allLines[^lines..]);
    }
}

/// <summary>
/// Обёртка dotnet CLI для сквозной NFR-верификации TS-181/TS-182 (FR-027):
/// метатесты запускают сборку и прогон бэкенд-тестов (src/api) дочерними
/// процессами dotnet. Детерминизм и изоляция прогона:
/// - глобальный межпроцессный замок (CR-003 арбитража a-034): замок
///   <see cref="SrcApiBuildLock"/> с единым зафиксированным путём удерживается
///   ВСЁ время жизни дочернего процесса — параллельные dotnet build/test над
///   тем же src/api из ДЕРЖАТЕЛЕЙ замка (B-20 SrcApiBuildLock и B-22
///   B22SrcApiBuildLock — тот же путь lock-файла; B-16 сериализуется только
///   xUnit-коллекцией) не пересекаются на obj/. ПОПРАВКА раунда доработки
///   CR-001: зона B-21 (Ts185_BuildGatesTests.cs, B21ProcessRunner) исполняет
///   dotnet build/test над тем же src/api БЕЗ захвата замка —
///   некоординированный участник; от пересечения с ней замок не защищает
///   (нужна сериализация зон на уровне диспетчеризации прогона);
/// - DOTNET_ROLL_FORWARD=LatestMajor — тестовый хост LabsApp.Tests собран под
///   net8.0, а на хосте прогона установлены только рантаймы 9/10 (приём
///   процессных тестов зоны B-01);
/// - DOTNET_USE_POLLING_FILE_WATCHER=true — файловые наблюдатели CLI/MSBuild
///   в polling-режиме (урок зоны B-01: лимит inotify-экземпляров на
///   пользователя исчерпывается при параллельных короткоживущих процессах);
/// - MSBUILDDISABLENODEREUSE=1 — MSBuild не оставляет переиспользуемые узлы:
///   они живут после выхода dotnet build, наследуют дескрипторы
///   перенаправленного stdout и вечно держат открытыми трубы вывода;
/// - телеметрия и логотип отключены — воспроизводимый вывод.
/// Устойчивость к аварийному снятию дочернего процесса (CR-004 арбитража
/// a-034): таймаут диагностируется ВОЗВРАЩАЕМЫМ результатом (TimedOut=true +
/// накопленный хвост вывода) без необработанного исключения из чтения
/// stdout/stderr — Task&lt;T&gt;.Result читается только у задач в состоянии
/// RanToCompletion. Тяжёлые прогоны дополнительно сериализуются коллекцией
/// xUnit «b18-backend-dotnet-cli» внутри зоны B-18.
/// </summary>
public static class DotNetCli
{
    /// <summary>
    /// Бюджет ожидания межпроцессного замка (доработка CR-001/CR-004),
    /// ОТДЕЛЁННЫЙ от бюджета дочернего процесса: держатели замка (B-20/B-22)
    /// исполняют над src/api вплоть до ПОЛНОГО dotnet test LabsApp.Tests
    /// (бюджет полного прогона Ts182 — 30 мин), поэтому запас ожидания замка
    /// заведомо больше самой долгой удерживаемой команды. Дочерний бюджет при
    /// этом остаётся соразмерен самой команде: «не взяли замок» и «дочерний
    /// процесс не уложился» классифицируются раздельно.
    /// </summary>
    public static readonly TimeSpan GenerousLockWait = TimeSpan.FromMinutes(40);

    /// <summary>
    /// Перегрузка совместимости: один бюджет и на ожидание замка, и на
    /// дочерний процесс (прежнее поведение). Для команд над src/api в дереве
    /// полного прогона используйте двухбюджетную форму
    /// <see cref="Run(IReadOnlyList{string}, TimeSpan, TimeSpan)"/> с
    /// <see cref="GenerousLockWait"/> — иначе ожидание замка съедает бюджет
    /// дочерней команды (CR-004).
    /// </summary>
    public static DotNetCliResult Run(IReadOnlyList<string> arguments, TimeSpan timeout) =>
        Run(arguments, timeout, timeout);

    public static DotNetCliResult Run(
        IReadOnlyList<string> arguments, TimeSpan lockWait, TimeSpan childTimeout)
    {
        // CR-003: замок берётся ДО старта дочернего процесса и держится до его
        // полного завершения (включая слив труб — слив может снимать
        // «внучатые» процессы, наследовавшие дескрипторы).
        var lockHandle = SrcApiBuildLock.TryAcquire(lockWait);
        if (lockHandle is null)
        {
            return new DotNetCliResult(
                ExitCode: -1,
                TimedOut: true,
                Output: "Не удалось захватить глобальный межпроцессный замок «"
                    + SrcApiBuildLock.LockFilePath + "» за " + lockWait.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " с: в другой тестовой зоне выполняется dotnet build/test над src/api "
                    + "(дочерний процесс не запускался, пересечения сборок на obj/ нет).");
        }

        using (lockHandle)
        {
            return RunChild(arguments, childTimeout);
        }
    }

    private static DotNetCliResult RunChild(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotNetMuxer(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        startInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
        startInfo.EnvironmentVariables["DOTNET_USE_POLLING_FILE_WATCHER"] = "true";
        // Утечка окружения SDK родителя: тест-хост зоны запущен SDK 10, который
        // прописывает MSBUILD_EXE_PATH/MSBuildSDKsPath/DOTNET_HOST_PATH в процесс.
        // Дочерний dotnet с этими переменными берёт ЗАДАЧИ SDK 10 при рантайме 8
        // (или наоборот) — MSB4062 «не удалось загрузить задачу». Переменные
        // снимаются: дочерний muxer резолвит SDK по собственному корню.
        startInfo.EnvironmentVariables["MSBUILD_EXE_PATH"] = null;
        startInfo.EnvironmentVariables["MSBuildSDKsPath"] = null;
        startInfo.EnvironmentVariables["MSBuildExtensionsPath"] = null;
        startInfo.EnvironmentVariables["DOTNET_HOST_PATH"] = null;
        // Рантайм net8.0 на хосте прогона есть только в пользовательской установке
        // (~/.dotnet): системный muxer содержит лишь 9.x/10.x, и LatestMajor-прокат
        // net8→net10 ломает WebApplicationFactory-тесты (AspNetCore.App 8→10:
        // ложные 500/иное поведение эндпойнтов на зелёном на net8 наборе).
        // Дочерним dotnet-процессам явно задаётся корень установки с 8.x
        // (DOTNET_ROOT_X64 имеет приоритет над DOTNET_ROOT — перекрываем оба).
        var dotnetRoot = ResolveDotNetRootWithNet8Runtime();
        if (dotnetRoot is not null)
        {
            startInfo.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
            startInfo.EnvironmentVariables["DOTNET_ROOT_X64"] = dotnetRoot;
        }
        startInfo.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Не удалось запустить дочерний процесс dotnet.");
        }

        // Чтение обоих потоков фоновыми задачами (без deadlock на трубах);
        // после завершения процесса хвосты сливаются с ограничением по
        // времени — трубу может удерживать уцелевший сторонний потомок.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            // Снять всё дерево (dotnet плодит узлы MSBuild/vstest), чтобы не
            // оставить runaway-процессы после падения теста по таймауту.
            process.Kill(entireProcessTree: true);
            process.WaitForExit(30_000);
            return new DotNetCliResult(
                ExitCode: process.HasExited ? process.ExitCode : -1,
                TimedOut: true,
                Output: Drain(stdoutTask) + Drain(stderrTask));
        }

        return new DotNetCliResult(
            ExitCode: process.ExitCode,
            TimedOut: false,
            Output: Drain(stdoutTask) + Drain(stderrTask));
    }

    /// <summary>Ограниченный по времени слив прочитанного потока (≤30 с).</summary>
    private static string Drain(Task<string> readTask)
    {
        try
        {
            readTask.Wait(TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            // поток читался в момент снятия процесса — берём что накопилось.
        }

        // CR-004: Result читается ТОЛЬКО у задач в состоянии RanToCompletion —
        // у Faulted/Canceled задач доступ к Result бросает исключение, а
        // незавершённая (но не ошибшаяся) задача заблокировала бы вызов.
        return readTask.Status == TaskStatus.RanToCompletion ? readTask.Result : string.Empty;
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
