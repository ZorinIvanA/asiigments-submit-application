namespace LabsApp.IntegrationTests.B19.Infrastructure;

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
/// Запуск внешних процессов (ng build / ng test — TS-172) с полным перехватом вывода
/// и жёстким тайм-аутом. Детерминирован: окружение наследуется, аргументы — одной
/// строкой без shell-интерполяции.
/// </summary>
public static class ProcessRunner
{
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
        // потомка. Даём выводу короткий льготный период, затем снимаем дерево.
        var drained = Task.WhenAll(standardOutputTask, standardErrorTask);
        if (await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(10))) != drained)
        {
            process.Kill(entireProcessTree: true);
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }
}
