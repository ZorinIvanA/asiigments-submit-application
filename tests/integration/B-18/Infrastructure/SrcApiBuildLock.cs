namespace LabsApp.IntegrationTests.B18.Infrastructure;

/// <summary>
/// Глобальный межпроцессный замок сериализации dotnet build/test над src/api
/// (CR-003 арбитража a-034, given TS-181/TS-182): одновременные MSBuild-сессии
/// одного проекта не должны пересекаться на obj/, когда в дереве прогона
/// параллельно исполняются тестовые зоны со своими хелперами запуска dotnet.
/// Фактические соучастники замка (проверено чтением исходников tests/, раунд
/// доработки CR-001): B-18 DotNetCli, B-20 SrcApiBuildLock и B-22
/// B22SrcApiBuildLock — тот же путь lock-файла; зона B-16 сериализует прогоны
/// только xUnit-коллекцией (SerialBuildAndProcessCollection) и замок не берёт.
/// ПОПРАВКА раунда доработки CR-001 (прежнее утверждение «B-21 общих
/// dotnet-прогонов над src/api не выполняет» было ложным): зона B-21
/// исполняет ДОЧЕРНИЕ dotnet build/test над src/api
/// (Scenarios/Ts185_BuildGatesTests.cs через B21ProcessRunner) БЕЗ захвата
/// этого замка — некоординированный участник dotnet-прогонов. Гарантия
/// «параллельные сборки не пересекаются на obj/» действует ТОЛЬКО между
/// держателями замка (B-18/B-20/B-22); от пересечения с B-21 защищает только
/// сериализация этих зон на уровне диспетчеризации прогона (вопрос
/// оркестратора — включить B-21/Ts185 в число держателей замка по образцу
/// B-20/B-22; в коде зоны B-18 это не решается). Зоны-соучастники обязаны
/// использовать ЕДИНЫЙ зафиксированный путь lock-файла — константа
/// <see cref="LockFilePath"/> публична и одинакова для любой зоны.
/// Эксклюзивный lock-файл по фиксированному пути репозитория выбран вместо
/// именованного mutex: поведение одинаково на Linux и Windows, а дескриптор
/// гарантированно освобождается ОС при аварийной смерти держателя. Файл
/// создаётся, но никогда не удаляется — гонки удаления с параллельным
/// держателем исключены.
/// </summary>
public static class SrcApiBuildLock
{
    /// <summary>
    /// Единое зафиксированное имя замка (путь в репозитории) для всех
    /// тестовых зон, запускающих dotnet build/test над src/api.
    /// </summary>
    public static readonly string LockFilePath =
        Path.Combine(RepoPaths.RepositoryRoot, ".dotnet-srcapi-build.lock");

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
