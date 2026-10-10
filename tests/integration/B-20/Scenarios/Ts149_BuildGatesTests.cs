using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

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
/// поэтому дочерним dotnet-командам выставляется DOTNET_ROLL_FORWARD=LatestMajor
/// (хелпер DotNetCli зоны B-20 — тот же, что у метатестов TS-181/TS-182, с
/// межпроцессным замком <see cref="SrcApiBuildLock"/> над obj/bin src/api).
/// ng-прогоны аналогично сериализуются межпроцессным замком
/// <see cref="NgCli.NgProcessLockFilePath"/> (единый путь .ng-cli-process.lock
/// для всех зон — ng пишут в общие .angular-кэш и dist).
/// Все четыре прогона включены в последовательную коллекцию зоны
/// «b20-backend-dotnet-cli»: они конкурируют за .angular-кэш/dist, obj/bin
/// src/api и CPU бенчмарка TS-148 — параллельность порождает ложные падения
/// и шум латентности.
/// </summary>
[Collection("b20-backend-dotnet-cli")]
public sealed class Ts149_BuildGatesTests
{
    private static readonly TimeSpan DotnetBuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DotnetTestTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NgBuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NgTestTimeout = TimeSpan.FromMinutes(15);

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
    public void DotnetBuild_Solution_ExitsZero_WithoutWarnings_WithTreatWarningsAsErrorsFixed()
    {
        // given: TreatWarningsAsErrors=true в csproj реализации и тестов бэкенда.
        foreach (var csproj in new[]
                 {
                     NfrGatePaths.ApiProjectFile,
                     NfrGatePaths.ApiTestsProjectFile,
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
        var result = DotNetCli.Run(
            ["build", NfrGatePaths.ApiSolutionFile, "--nologo", "-v", "minimal"],
            DotnetBuildTimeout);

        // then: сборка успешна и без предупреждений.
        Assert.True(
            result.Succeeded,
            $"Ожидался код 0 у dotnet build (NFR-002), фактически {result.ExitCode} "
            + $"(timedOut={result.TimedOut}). Вывод:{Environment.NewLine}{result.OutputTail()}");
        var warnings = result.Output
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
    public void DotnetTest_BackendTests_ExitsZero()
    {
        Assert.True(
            File.Exists(NfrGatePaths.ApiTestsProjectFile),
            $"Предусловие кейса: не найден тест-проект бэкенда {NfrGatePaths.ApiTestsProjectFile}.");

        // when: dotnet test тест-проекта бэкенда (зона tests/integration/B-20 в
        // решение не входит — рекурсии прогона нет).
        var result = DotNetCli.Run(
            ["test", NfrGatePaths.ApiTestsProjectFile, "--nologo", "-v", "q"],
            DotnetTestTimeout);

        // then: exit 0.
        Assert.True(
            result.Succeeded,
            $"Ожидался код 0 у dotnet test (NFR-002), фактически {result.ExitCode} "
            + $"(timedOut={result.TimedOut}). Вывод:{Environment.NewLine}{result.OutputTail()}");
    }

    /// <summary>TS-149 / when (часть 3): «ng build»; then: exit 0.</summary>
    [Fact]
    public async Task NgBuild_Client_ExitsZero()
    {
        var run = await NgCli.RunNgAsync("build", (int)NgBuildTimeout.TotalMilliseconds);

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
            (int)NgTestTimeout.TotalMilliseconds);

        Assert.True(
            run.ExitCode == 0,
            $"Ожидался код 0 у ng test --watch=false --browsers=ChromeHeadless (NFR-002), "
            + $"фактически {run.ExitCode}. Вывод:{Environment.NewLine}{run.OutputTail()}");
    }
}
