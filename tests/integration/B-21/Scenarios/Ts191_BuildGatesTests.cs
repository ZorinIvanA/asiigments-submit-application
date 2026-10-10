using LabsApp.IntegrationTests.B21.Infrastructure;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-191 (NFR-002, P1): dotnet build — 0 предупреждений (TreatWarningsAsErrors).
///
/// given: файлы проекта src/api/*.csproj (все проекты решения src/api —
///        реализация и тесты реализации); TreatWarningsAsErrors=true
///        зафиксирован в каждом.
/// when:  dotnet build решения src/api (дочерний процесс под единым
///        межпроцессным замком src/api — <see cref="B21DotNetCli"/>:
///        захват до старта дочернего процесса, удержание до его полного
///        завершения; параллельные зоны не пересекаются на obj/).
/// then:  Exit 0, 0 предупреждений; TreatWarningsAsErrors=true зафиксирован
///        в csproj (NFR-002).
///
/// Строка предупреждения компилятора/инструментов — «…: warning CS1234: …»
/// (в т.ч. NuGet NU1xxx); строки сводки вида «    0 Warning(s)» предупреждениями
/// НЕ считаются (нет кода) — опознание вынесено в общий помощник зоны
/// <see cref="B21BuildGateWarnings"/> (REWORK CR-002: без приватных копий).
/// Дочерним dotnet-командам выставляется
/// DOTNET_ROLL_FORWARD=LatestMajor (на хосте прогона нет runtime net8.0 —
/// <see cref="B21ProcessRunner.ApplyCommonEnvironment"/>).
/// </summary>
public sealed class Ts191_BuildGatesTests
{
    private const int DotnetBuildTimeoutMs = 600_000;

    [Fact]
    public async Task TreatWarningsAsErrorsFixed_InEverySrcApiCsproj_And_DotnetBuild_ExitsZero_WithZeroWarnings()
    {
        // given: файлы проекта src/api/*.csproj — TreatWarningsAsErrors=true
        // зафиксирован в КАЖДОМ (генерируемые проекты в bin/obj исключены).
        var srcApiDirectory = Path.Combine(B21RepoPaths.RepositoryRoot, "src", "api");
        var csprojs = Directory
            .EnumerateFiles(srcApiDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            csprojs.Length > 0,
            $"Предусловие кейса: в {srcApiDirectory} не найден ни один файл *.csproj.");
        foreach (var csproj in csprojs)
        {
            Assert.True(
                File.ReadAllText(csproj).Contains(
                    "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", StringComparison.Ordinal),
                $"NFR-002: TreatWarningsAsErrors=true не зафиксирован в {csproj}.");
        }

        // when: dotnet build (решение src/api — реализация и тесты реализации).
        var result = await B21DotNetCli.RunAsync(
            ["build", B21RepoPaths.ApiSolutionPath, "--nologo"],
            DotnetBuildTimeoutMs);

        // then: Exit 0 и 0 предупреждений.
        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet build (NFR-002), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.Tail()}");
        var warnings = B21BuildGateWarnings.Collect(result.Output);
        Assert.True(
            warnings.Count == 0,
            $"Ожидался dotnet build без предупреждений (NFR-002), найдено {warnings.Count}:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, warnings.Take(20)));
    }
}
