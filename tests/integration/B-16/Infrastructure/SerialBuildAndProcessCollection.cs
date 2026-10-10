namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Последовательная коллекция всех тестовых классов зоны: детерминированный порядок
/// выполнения без параллельных сессий MSBuild над общим obj/bin LabsApp — файловые
/// блокировки и ложные нестабильные падения исключены (урок BL-001). Внутри
/// коллекции xUnit выполняет тесты строго по одному.
/// </summary>
[CollectionDefinition(SerialBuildAndProcessCollection.Name)]
public sealed class SerialBuildAndProcessCollection
{
    public const string Name = "B16.SerialBuildAndProcess";
}
