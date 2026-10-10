using LabsApp.IntegrationTests.B21.Infrastructure;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-185/TS-149 (NFR-002, P1): сборки и тесты проходят чисто.
///
/// Статус файла: копия буквы NFR-002 в зоне B-21 по описи coverage_check
/// (канонический dotnet-держатель — tests/integration/B-22,
/// Scenarios/Ts185_BuildGatesTests.cs). По a-124/CR-001 прогон этой копии
/// амендирован обязательным захватом ЕДИНОГО межпроцессного замка src/api
/// (<see cref="B21SrcApiBuildLock.LockFilePath"/>, тот же путь
/// .dotnet-srcapi-build.lock, что у зон B-18/B-20/B-22): захват до старта
/// дочернего процесса, удержание до его полного завершения
/// (<see cref="B21DotNetCli"/>) — пересечения MSBuild-сессий на obj/ с
/// параллельными зонами исключены. ng build/ng test исполняются над src/client
/// (другое дерево проектов) и в замке src/api не участвуют.
///
/// REWORK CR-001/CR-002 батча B-21: часть 1 этого класса («dotnet build —
/// 0 предупреждений») УДАЛЕНА как дословный дубль гейта TS-191 — единственный
/// держатель dotnet build-гейта зоны теперь <see cref="Ts191_BuildGatesTests"/>
/// (его given сильнее: TreatWarningsAsErrors проверяется во ВСЕХ *.csproj
/// src/api, а не в двух файлах); у этого класса остаются непересекающиеся
/// части «dotnet test», «ng build», «ng test» — один класс на сценарный ID,
/// без двойного dotnet build под межпроцессным замком.
///
/// given: решение в репозитории (src/api/LabsApp.sln; workspace клиента — корень
///        репозитория); TreatWarningsAsErrors зафиксирован в csproj
///        (гейт <see cref="Ts191_BuildGatesTests"/>).
/// when:  dotnet test в src/api; ng build, ng test в src/client (дочерние
///        процессы; ng — из корня workspace через node_modules/@angular/cli).
/// then:  dotnet test, ng build, ng test — exit 0 (NFR-002); dotnet build —
///        0 предупреждений — гейт TS-191.
///
/// Замечания по запуску: на хосте прогона нет runtime net8.0 (только 9.x/10.x),
/// поэтому дочерним dotnet-командам выставляется DOTNET_ROLL_FORWARD=LatestMajor;
/// ng test — дословно «ng test --watch=false --browsers=ChromeHeadless» (watch и
/// реальный браузер несовместимы с CI-прогоном; CHROME_BIN выставляется только
/// если не задан). Зона тестов батча (tests/integration/B-21) в проверяемые
/// проекты не входит — рекурсии прогона нет.
/// </summary>
public sealed class Ts185_BuildGatesTests
{
    private const int DotnetTestTimeoutMs = 1_200_000;
    private const int NgBuildTimeoutMs = 900_000;
    private const int NgTestTimeoutMs = 900_000;

    /// <summary>TS-185 / when (часть 2): «dotnet test в src/api»; then: exit 0.</summary>
    [Fact]
    public async Task DotnetTest_SrcApi_ExitsZero()
    {
        Assert.True(
            File.Exists(B21RepoPaths.ApiTestProjectPath),
            $"Предусловие кейса: не найден тест-проект реализации {B21RepoPaths.ApiTestProjectPath}.");

        var result = await B21DotNetCli.RunAsync(
            ["test", B21RepoPaths.ApiTestProjectPath, "--nologo", "-v", "q"],
            DotnetTestTimeoutMs);

        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet test (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.Tail()}");
    }

    /// <summary>TS-185 / when (часть 3): «ng build в src/client»; then: exit 0.</summary>
    [Fact]
    public async Task NgBuild_Client_ExitsZero()
    {
        var result = await B21NgCli.RunAsync("build", NgBuildTimeoutMs);

        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у ng build (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.Tail()}");
    }

    /// <summary>TS-185 / when (часть 4): «ng test в src/client»; then: exit 0.</summary>
    [Fact]
    public async Task NgTest_Client_ExitsZero()
    {
        var result = await B21NgCli.RunAsync(
            "test --watch=false --browsers=ChromeHeadless",
            NgTestTimeoutMs);

        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у ng test --watch=false --browsers=ChromeHeadless (NFR-002), "
            + $"фактически {result.ExitCode}. Вывод:{Environment.NewLine}{result.Tail()}");
    }
}
