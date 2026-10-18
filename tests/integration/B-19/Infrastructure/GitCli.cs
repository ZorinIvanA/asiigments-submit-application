namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Запуск git (TS-178 — чтение эталонных версий сервисов из HEAD ветки;
/// TS-196 — git diff error-texts.ts). Рабочая директория фиксирована ключом
/// -C (корень репозитория), аргументы передаются одной строкой без
/// shell-интерполяции. Вывод перехватывается полностью.
/// </summary>
public static class GitCli
{
    private static readonly string[] GitCandidates = ["git", "/usr/bin/git", "/usr/local/bin/git"];

    /// <summary>Выполнить git с указанными аргументами в корне репозитория.</summary>
    public static ProcessResult Run(string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveGitPath(),
            Arguments = $"-C \"{RepoPaths.RepositoryRoot}\" {arguments}",
            WorkingDirectory = RepoPaths.RepositoryRoot,
        };

        return ProcessRunner
            .RunAsync(startInfo, (int)timeout.TotalMilliseconds)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Содержимое файла в версии HEAD ветки (эталон для TS-178:
    /// «методы и типы публичного API неизменны относительно эталона ветки»).
    /// </summary>
    public static string ShowHeadBlob(string repoRelativePath)
    {
        var result = Run($"show \"HEAD:{repoRelativePath}\"", TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git show HEAD:{repoRelativePath} завершился с кодом {result.ExitCode}: "
                + result.OutputTail());
        }

        return result.StandardOutput;
    }

    /// <summary>
    /// git diff HEAD для одного файла: пустой вывод и код 0 — файл не изменён
    /// относительно эталона ветки (TS-196).
    /// </summary>
    public static ProcessResult DiffHead(string repoRelativePath) =>
        Run($"diff HEAD -- \"{repoRelativePath}\"", TimeSpan.FromMinutes(2));

    private static string ResolveGitPath()
    {
        foreach (var candidate in GitCandidates)
        {
            if (candidate == "git")
            {
                // Разрешение по PATH: пытаемся запустить «git --version».
                var probe = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                };
                try
                {
                    using var process = Process.Start(probe);
                    if (process is not null)
                    {
                        process.WaitForExit(10_000);
                        if (process.HasExited && process.ExitCode == 0)
                        {
                            return candidate;
                        }
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Файла «git» в PATH нет — пробуем следующий кандидат.
                }

                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Не найден исполняемый файл git (PATH, /usr/bin, /usr/local/bin).");
    }
}
