namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Общий механизм межпроцессных замков тестовых зон по lock-файлу в корне
/// репозитория: эксклюзивный дескриптор по ЕДИНОМУ зафиксированному пути
/// (образцы потребителей — <see cref="SrcApiBuildLock"/> для dotnet build/test
/// над src/api и ng-замок <see cref="NgCli"/> для ng build/test). Эксклюзивный
/// lock-файл по фиксированному пути репозитория выбран вместо именованного
/// mutex: поведение одинаково на Linux и Windows, а дескриптор гарантированно
/// освобождается ОС при аварийной смерти держателя. Файл создаётся, но никогда
/// не удаляется — гонки удаления с параллельным держателем исключены.
/// Межзонная защита действует для всех зон, использующих тот же путь замка.
/// </summary>
public static class RepoRootFileLock
{
    /// <summary>
    /// Захватить замок по заданному пути, при необходимости ожидая до
    /// <paramref name="waitFor"/>. Возвращает null, если замок не освободился
    /// за отведённое время (его удерживает конкурирующий процесс).
    /// </summary>
    public static IDisposable? TryAcquire(string lockFilePath, TimeSpan waitFor)
    {
        var deadlineUtc = DateTime.UtcNow + waitFor;
        while (true)
        {
            try
            {
                return new LockHandle(new FileStream(
                    lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                // Замок удержан другим процессом (чужая зона/контур).
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    return null;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    private sealed class LockHandle(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }
}
