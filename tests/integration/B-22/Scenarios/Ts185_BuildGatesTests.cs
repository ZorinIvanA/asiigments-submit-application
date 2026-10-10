using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B22.Infrastructure;

namespace LabsApp.IntegrationTests.B22.Scenarios;

/// <summary>
/// TS-185 (NFR-002, P1): сборки и тесты проходят чисто.
///
/// given: решение в репозитории (src/api/LabsApp.sln; workspace клиента — корень
///        репозитория); TreatWarningsAsErrors зафиксирован в csproj.
/// when:  dotnet build, dotnet test в src/api; ng build, ng test в src/client
///        (дочерние процессы; ng — из корня workspace через node_modules/@angular/cli).
/// then:  dotnet build — 0 предупреждений (TreatWarningsAsErrors=true не ломает
///        сборку); dotnet test, ng build, ng test — exit 0 (NFR-002).
///
/// Замечания по запуску: на хосте прогона нет runtime net8.0 (только 9.x/10.x),
/// поэтому дочерним dotnet-командам выставляется DOTNET_ROLL_FORWARD=LatestMajor
/// и вся сборка src/api идёт под единым межпроцессным замком
/// <see cref="B22SrcApiBuildLock"/> (конвенция зоны B-18 — параллельные зоны не
/// пересекаются на obj/); ng test — дословно «ng test --watch=false
/// --browsers=ChromeHeadless» (watch и реальный браузер несовместимы с CI-прогоном;
/// CHROME_BIN выставляется только если не задан). Зона тестов батча
/// (tests/integration/B-22) в проверяемые проекты не входит — рекурсии прогона нет.
/// </summary>
public sealed class Ts185_BuildGatesTests
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
    /// TS-185 / given+when (часть 1): TreatWarningsAsErrors зафиксирован в csproj
    /// src/api; «dotnet build» — «0 предупреждений (TreatWarningsAsErrors=true не
    /// ломает сборку)».
    /// </summary>
    [Fact]
    public async Task DotnetBuild_SrcApi_ExitsZero_WithoutWarnings_WithTreatWarningsAsErrorsFixed()
    {
        // given: TreatWarningsAsErrors=true в csproj реализации и тестов src/api.
        foreach (var csproj in new[] { B22RepoPaths.ApiProjectPath, B22RepoPaths.ApiTestsProjectPath })
        {
            Assert.True(
                File.Exists(csproj),
                $"Предусловие кейса: не найден файл проекта {csproj}.");
            Assert.True(
                File.ReadAllText(csproj).Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", StringComparison.Ordinal),
                $"Предусловие NFR-002: TreatWarningsAsErrors не зафиксирован в {csproj}.");
        }

        // when: dotnet build src/api/LabsApp.sln (под единым межпроцессным замком).
        var result = await B22DotNetCli.RunAsync(
            ["build", B22RepoPaths.ApiSolutionPath, "--nologo"],
            DotnetBuildTimeoutMs);

        // then: сборка успешна и без предупреждений.
        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet build, фактически {result.ExitCode}. Вывод:{Environment.NewLine}{result.Tail()}");
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

    /// <summary>TS-185 / when (часть 2): «dotnet test в src/api»; then: exit 0.</summary>
    [Fact]
    public async Task DotnetTest_SrcApi_ExitsZero()
    {
        Assert.True(
            File.Exists(B22RepoPaths.ApiTestProjectPath),
            $"Предусловие кейса: не найден тест-проект реализации {B22RepoPaths.ApiTestProjectPath}.");

        var result = await B22DotNetCli.RunAsync(
            ["test", B22RepoPaths.ApiTestProjectPath, "--nologo", "-v", "q"],
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
        var result = await B22NgCli.RunAsync("build", NgBuildTimeoutMs);

        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у ng build (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.Tail()}");
    }

    /// <summary>TS-185 / when (часть 4): «ng test в src/client»; then: exit 0.</summary>
    [Fact]
    public async Task NgTest_Client_ExitsZero()
    {
        var result = await B22NgCli.RunAsync(
            "test --watch=false --browsers=ChromeHeadless",
            NgTestTimeoutMs);

        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у ng test --watch=false --browsers=ChromeHeadless (NFR-002), "
            + $"фактически {result.ExitCode}. Вывод:{Environment.NewLine}{result.Tail()}");
    }
}
