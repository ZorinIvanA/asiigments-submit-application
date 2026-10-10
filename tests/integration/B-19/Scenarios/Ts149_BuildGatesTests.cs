using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// TS-149 (NFR-002, P1): сборки без предупреждений, все прогоны зелёные.
///
/// given: решение бэкенда и клиента собраны; в csproj зафиксирован
///        TreatWarningsAsErrors=true (проверяется в given первого теста).
/// when:  dotnet build; dotnet test; ng build; ng test (дочерние процессы;
///        ng — из корня workspace через node_modules/@angular/cli, дословно
///        «ng test --watch=false --browsers=ChromeHeadless» — watch и реальный
///        браузер несовместимы с CI-прогоном).
/// then:  dotnet build — 0 предупреждений (TreatWarningsAsErrors подтверждён в
///        csproj); dotnet test, ng build, ng test — exit 0 (NFR-002, методика
///        verification: CI-команды локально).
///
/// Замечания по запуску: на хосте прогона нет runtime net8.0 (только 9.x/10.x),
/// поэтому дочерним dotnet-командам выставляется DOTNET_ROLL_FORWARD=LatestMajor.
/// Все четыре прогона включены в SerialNgProcessCollection зоны: они конкурируют
/// за .angular-кэш/dist и за obj/bin src/api с ng serve+Kestrel (TS-180) и
/// бенчмарком TS-148 — параллельность порождает ложные падения и шум латентности.
/// </summary>
[Collection(SerialNgProcessCollection.Name)]
public sealed class Ts149_BuildGatesTests
{
    private const int DotnetBuildTimeoutMs = 600_000;
    private const int DotnetTestTimeoutMs = 1_200_000;
    private const int NgBuildTimeoutMs = 900_000;
    private const int NgTestTimeoutMs = 900_000;

    /// <summary>
    /// Строка предупреждения компилятора/инструментов в консоли MSBuild/NuGet:
    /// «…: warning CS1234: …» (в т.ч. NuGet NU1xxx). Строки сводки вида
    /// «    0 Warning(s)» предупреждениями НЕ считаются (нет кода).
    /// </summary>
    private static readonly Regex WarningLine = new(
        @"\b(warning|предупреждение)\s+[A-Z]+\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// TS-149 / given+when (часть 1): TreatWarningsAsErrors=true зафиксирован в
    /// csproj бэкенда; «dotnet build» — 0 предупреждений.
    /// </summary>
    [Fact]
    public async Task DotnetBuild_Solution_ExitsZero_WithoutWarnings_WithTreatWarningsAsErrorsFixed()
    {
        // given: TreatWarningsAsErrors=true в csproj реализации и тестов бэкенда.
        foreach (var csproj in new[]
                 {
                     RepoPaths.ApiProjectFile,
                     RepoPaths.ApiTestsProjectFile,
                 })
        {
            Assert.True(
                File.Exists(csproj),
                $"Предусловие кейса: не найден файл проекта {csproj}.");
            Assert.True(
                File.ReadAllText(csproj).Contains(
                    "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>",
                    StringComparison.Ordinal),
                $"Предусловие NFR-002: TreatWarningsAsErrors не зафиксирован в {csproj}.");
        }

        // when: dotnet build решения бэкенда.
        var result = await DotNetCli.RunAsync(
            ["build", RepoPaths.ApiSolutionFile, "--nologo"],
            RepoPaths.RepositoryRoot,
            DotnetBuildTimeoutMs);

        // then: сборка успешна и без предупреждений.
        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet build (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.OutputTail()}");
        var warnings = result.CombinedOutput
            .Split('\n')
            .Where(line => WarningLine.IsMatch(line))
            .ToList();
        Assert.True(
            warnings.Count == 0,
            $"Ожидался dotnet build без предупреждений (NFR-002), найдено {warnings.Count}:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, warnings.Take(20)));
    }

    /// <summary>TS-149 / when (часть 2): «dotnet test»; then: exit 0.</summary>
    [Fact]
    public async Task DotnetTest_BackendTests_ExitsZero()
    {
        Assert.True(
            File.Exists(RepoPaths.ApiTestsProjectFile),
            $"Предусловие кейса: не найден тест-проект бэкенда {RepoPaths.ApiTestsProjectFile}.");

        // when: dotnet test тест-проекта бэкенда (зона tests/integration/B-19 в
        // решение не входит — рекурсии прогона нет).
        var result = await DotNetCli.RunAsync(
            ["test", RepoPaths.ApiTestsProjectFile, "--nologo", "-v", "q"],
            RepoPaths.RepositoryRoot,
            DotnetTestTimeoutMs);

        // then: exit 0.
        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet test (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.OutputTail()}");
    }

    /// <summary>TS-149 / when (часть 3): «ng build»; then: exit 0.</summary>
    [Fact]
    public async Task NgBuild_Client_ExitsZero()
    {
        var run = await NgCli.RunNgAsync("build", NgBuildTimeoutMs);

        Assert.True(
            run.ExitCode == 0,
            $"Ожидался код 0 у ng build (NFR-002), фактически {run.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{run.OutputTail()}");
    }

    /// <summary>TS-149 / when (часть 4): «ng test»; then: exit 0.</summary>
    [Fact]
    public async Task NgTest_Client_ExitsZero()
    {
        var run = await NgCli.RunNgAsync(
            "test --watch=false --browsers=ChromeHeadless",
            NgTestTimeoutMs);

        Assert.True(
            run.ExitCode == 0,
            $"Ожидался код 0 у ng test --watch=false --browsers=ChromeHeadless (NFR-002), "
            + $"фактически {run.ExitCode}. Вывод:{Environment.NewLine}{run.OutputTail()}");
    }
}
