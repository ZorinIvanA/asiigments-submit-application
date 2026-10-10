using System.Diagnostics;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;

    /// <summary>
    /// Хвост комбинированного вывода для диагностических сообщений ассертов
    /// (полный вывод ng build / ng test достигает десятков килобайт).
    /// </summary>
    public string OutputTail(int maxCharacters = 4000)
    {
        var output = CombinedOutput;
        return output.Length <= maxCharacters ? output : "…" + output[^maxCharacters..];
    }
}

/// <summary>
/// Запуск внешних процессов (ng build / ng test — ворота TS-149/NFR-002) с полным
/// перехватом вывода и жёстким тайм-аутом. Детерминирован: окружение наследуется,
/// аргументы — одной строкой без shell-интерполяции. Копия механики зоны B-19
/// (чужая зона недоступна для ссылок — BL-001 BUG-001).
/// Страховка от «внучатых» процессов (esbuild-воркеры Angular CLI, наследующие
/// перехваченные трубы): после выхода прямого потомка слив ограничен
/// СОБСТВЕННЫМ коротким льготным периодом и потолком на каждую трубу —
/// уцелевшие потомки на Linux переподчиняются init и для Kill дерева уже
/// завершившегося корня недостижимы, поэтому ожидание не должно упираться в
/// дальний CTS процессного таймаута (иначе вместо диагностического результата
/// — отмена через весь остаток таймаута прогона). Недослитые трубы
/// фиксируются диагностикой в StandardError результата.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Льготный период слива вывода после выхода прямого потомка.</summary>
    private static readonly TimeSpan DrainGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>Собственный потолок ожидания каждой трубы при финальном сливе.</summary>
    private static readonly TimeSpan DrainPerPipeCap = TimeSpan.FromSeconds(15);

    public static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Не удалось запустить процесс: {startInfo.FileName} {startInfo.Arguments}.");

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
                $"Процесс «{startInfo.FileName} {startInfo.Arguments}» не завершился за {timeoutMilliseconds} мс.");
        }

        // Страховка: «внучатые» процессы (esbuild-воркеры Angular CLI) наследуют
        // перехваченные потоки и могут держать их открытыми после выхода прямого
        // потомка. Даём выводу короткий льготный период; не уложившимся трубам —
        // best-effort снятие дерева (пережившие корень потомки на Linux
        // переподчинены init, Kill их может не достичь) и диагностика ниже.
        var drained = Task.WhenAll(standardOutputTask, standardErrorTask);
        var drainedInGrace = await Task.WhenAny(drained, Task.Delay(DrainGracePeriod)) == drained;
        if (!drainedInGrace)
        {
            TryKillTree(process);
        }

        var (standardOutput, standardOutputDrained) = await ReadDrained(standardOutputTask);
        var (standardError, standardErrorDrained) = await ReadDrained(standardErrorTask);
        if (!standardOutputDrained || !standardErrorDrained)
        {
            var undrainedPipes = new List<string>(2);
            if (!standardOutputDrained)
            {
                undrainedPipes.Add("stdout");
            }

            if (!standardErrorDrained)
            {
                undrainedPipes.Add("stderr");
            }

            standardError += Environment.NewLine
                + "[ProcessRunner] Вывод процесса «" + startInfo.FileName + " " + startInfo.Arguments
                + "» слит не полностью (трубы " + string.Join(", ", undrainedPipes)
                + " удерживают пережившие корень потомки, Kill дерева завершившегося процесса "
                + "их не достигает); содержание недослитых труб в результате отсутствует.";
        }

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    /// <summary>Best-effort снятие дерева: процесс мог уже освободить дескриптор.</summary>
    private static void TryKillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException)
        {
            // Процесс уже завершён и дескриптор освобождён — снимать нечего.
        }
    }

    /// <summary>
    /// Ограниченный СОБСТВЕННЫМ коротким потолком (не дальним CTS процесса)
    /// слив прочитанного потока. Возвращает прочитанное и признак успеха:
    /// трубу может удерживать уцелевший сторонний потомок — тогда содержимое
    /// недоступно, и ожидание не должно блокировать результат до конца
    /// процессного таймаута.
    /// </summary>
    private static async Task<(string Content, bool Drained)> ReadDrained(Task<string> readTask)
    {
        var completed = await Task.WhenAny(readTask, Task.Delay(DrainPerPipeCap));
        return completed == readTask && readTask.Status == TaskStatus.RanToCompletion
            ? (readTask.Result, true)
            : (string.Empty, false);
    }
}
