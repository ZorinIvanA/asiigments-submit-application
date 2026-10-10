namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Последовательная коллекция для тестов, запускающих процессы dotnet run над src/api:
/// параллельные MSBuild-сессии над одним obj/bin дают файловые блокировки и ложные
/// нестабильные падения (урок BL-001). Внутри коллекции xUnit выполняет тесты строго
/// по одному; WAF-тесты других классов с ней не пересекаются по файлам.
/// </summary>
[CollectionDefinition(B02SerialProcessCollection.Name)]
public sealed class B02SerialProcessCollection
{
    public const string Name = "B02.SerialProcess";
}
