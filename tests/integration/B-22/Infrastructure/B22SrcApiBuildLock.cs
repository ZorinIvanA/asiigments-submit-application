namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>
/// Глобальный межпроцессный замок сериализации dotnet build/test над src/api
/// (конвенция зоны B-18, CR-003 арбитража a-034): одновременные MSBuild-сессии
/// одного проекта не должны пересекаться на obj/, когда в дереве прогона
/// параллельно исполняются тестовые зоны со своими хелперами запуска dotnet
/// (B-18 DotNetCli, B-16 ProcessRunner, B-21 ProcessRunner, зона B-22). Все зоны
/// обязаны использовать ЕДИНЫЙ зафиксированный путь lock-файла — константа
/// <see cref="LockFilePath"/> публична и одинакова для любой зоны.
/// Эксклюзивный lock-файл по фиксированному пути репозитория выбран вместо
/// именованного mutex: поведение одинаково на Linux и Windows, а дескриптор
/// гарантированно освобождается ОС при аварийной смерти держателя. Файл
/// создаётся, но никогда не удаляется — гонки удаления с параллельным
/// держателем исключены.
/// </summary>
public static class B22SrcApiBuildLock
{
    /// <summary>
    /// Единое зафиксированное имя замка (путь в репозитории) для всех
    /// тестовых зон, запускающих dotnet build/test над src/api.
    /// </summary>
    public static readonly string LockFilePath =
        Path.Combine(B22RepoPaths.RepositoryRoot, ".dotnet-srcapi-build.lock");

    /// <summary>
    /// Захватить замок, при необходимости ожидая до <paramref name="waitFor"/>.
    /// Возвращает null, если замок не освободился за отведённое время
    /// (конкурирующая зона выполняет dotnet build/test над src/api).
    /// </summary>
    public static IDisposable? TryAcquire(TimeSpan waitFor)
    {
        var stream = TryOpenExclusive(waitFor);
        return stream is null ? null : new LockHandle(stream);
    }

    private static FileStream? TryOpenExclusive(TimeSpan waitFor)
    {
        var deadlineUtc = DateTime.UtcNow + waitFor;
        while (true)
        {
            try
            {
                return new FileStream(
                    LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Замок удержан другим процессом (чужая зона собирает src/api).
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
