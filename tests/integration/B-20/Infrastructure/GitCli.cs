using System.Diagnostics;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Запуск git (TS-143 — чтение эталонных версий сервисов core из HEAD ветки).
/// Рабочая директория фиксирована ключом -C (корень репозитория), аргументы
/// передаются одной строкой без shell-интерполяции. Вывод перехватывается
/// полностью. Копия механики зоны B-19 (чужая зона недоступна для ссылок —
/// BL-001 BUG-001).
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
    /// Содержимое файла в версии HEAD ветки (эталон для TS-143:
    /// «публичный API сервисов неизменен» относительно эталона ветки).
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
