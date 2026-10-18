namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Глобальный межпроцессный замок сериализации dotnet build/test над src/api
/// (инфраструктура детерминизма прогона TS-181/TS-182): одновременные
/// MSBuild-сессии одного проекта не должны пересекаться на obj/, когда в
/// дереве прогона параллельно исполняются тестовые зоны со своими хелперами
/// запуска dotnet. Все зоны обязаны использовать ЕДИНЫЙ зафиксированный путь
/// lock-файла — константа <see cref="LockFilePath"/> публична и одинакова для
/// любой зоны. Механика захвата — общий <see cref="RepoRootFileLock"/>.
/// Буква кейса TS-181/TS-182 (переиздан a-048 по CR-003) межзонные гарантии
/// НЕ утверждает — замок здесь только механизм детерминизма собственного
/// прогона зоны B-20.
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
    public static IDisposable? TryAcquire(TimeSpan waitFor) =>
        RepoRootFileLock.TryAcquire(LockFilePath, waitFor);
}
