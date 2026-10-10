namespace LabsApp.IntegrationTests.B18Profile.Infrastructure;

/// <summary>
/// Последовательная коллекция всех тестовых классов зоны B-18/backend:
/// детерминированный порядок выполнения без параллельных сессий MSBuild над
/// общим obj/bin LabsApp — файловые блокировки и ложные нестабильные падения
/// исключены (урок BL-001). Внутри коллекции xUnit выполняет тесты строго
/// по одному.
/// </summary>
[CollectionDefinition(B18ProfileSerialCollection.Name)]
public sealed class B18ProfileSerialCollection
{
    public const string Name = "B18Profile.SerialBuildAndProcess";
}
