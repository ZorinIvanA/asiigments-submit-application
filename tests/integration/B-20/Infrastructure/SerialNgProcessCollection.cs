namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Последовательная коллекция для тестов, запускающих процессы ng build (TS-141),
/// ng test (TS-142) и стек ng serve + Kestrel (TS-144) над одним деревом исходников
/// клиента: параллельные CLI-сессии конкурируют за .angular-кэш, dist и CPU, порождая
/// ложные нестабильные падения (урок BL-001 зоны B-19). Внутри коллекции xUnit
/// выполняет тесты строго по одному. От тестов другой коллекции зоны
/// («b20-backend-dotnet-cli»: TS-149 и метатесты), тоже запускающих ng, защита —
/// межпроцессный замок <see cref="NgCli.NgProcessLockFilePath"/>; долгоживущий
/// ng serve сценария TS-144 удерживает тот же замок всё время жизни фикстуры
/// <see cref="B20DevStackFixture"/>.
/// </summary>
[CollectionDefinition(SerialNgProcessCollection.Name)]
public sealed class SerialNgProcessCollection
{
    public const string Name = "b20-client-ng-serial";
}
