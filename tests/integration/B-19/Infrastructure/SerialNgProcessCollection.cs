namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Последовательная коллекция для тестов, запускающих процессы ng build (TS-176),
/// ng test (TS-177) и стек ng serve + Kestrel (TS-180) над одним деревом исходников
/// клиента: параллельные CLI-сессии конкурируют за .angular-кэш, dist и CPU, порождая
/// ложные нестабильные падения (урок BL-001). Внутри коллекции xUnit выполняет тесты
/// строго по одному.
/// </summary>
[CollectionDefinition(SerialNgProcessCollection.Name)]
public sealed class SerialNgProcessCollection
{
    public const string Name = "B19.SerialNgProcess";
}
