using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// Кейс TS-146 (текущая нумерация раунда; историческая нумерация — TS-182,
/// переиздан a-034) «Unit-гейты: dotnet test зелёный, KDF-матрица входа
/// утверждена» — прогонная половина кейса: «Exit 0; покрытие затрагивает
/// гейты (1)–(8) FR-027» (P0, FR-027 AC «Полный прогон»).
/// given: решение src/api/LabsApp.sln и тестовый проект src/api/LabsApp.Tests
///        существуют; метатесты — в зоне tests/integration/B-18 (тот же хелпер
///        DotNetCli с глобальным межпроцессным замком CR-003 и диагностикой
///        таймаута CR-004, что и в TS-181; бюджет ожидания замка отделён от
///        бюджета дочернего процесса — доработка CR-001/CR-004: держатели
///        замка B-20/B-22 исполняют над src/api вплоть до полного dotnet
///        test, зона B-21 — без замка, некоординированный участник);
/// when:  (1) dotnet build LabsApp.sln; (2) полный dotnet test LabsApp.Tests
///        --no-build; (3) анализ покрытия пунктов (1)–(8) НЕВАКУУМНЫМИ
///        маркерами (CR-002): совместные осмысленные комбинации — маршруты
///        как пути «/labs»…«/submissions» (подстрока «lab» в имени проекта не
///        считается), 400/404/500 только в контексте конверта (не голые
///        цифры), у лимитера — движок окна + потолок MaxTrackedKeys/overflow +
///        граница 5/6 в связке с метками; (4) негативные пробы различимости:
///        контрольный текст без тестов лимитера → «гейт (2) не покрыт», без
///        тестов labs → «гейт (6) не покрыт», без тестов конверта ошибок →
///        «гейт (8) не покрыт»;
/// then:  build/test Exit 0; каждый из пунктов (1)–(8) признан покрытым;
///        проверка присутствия не проходит на побочном тексте исходников.
/// </summary>
[Collection("b18-backend-dotnet-cli")]
public sealed class Ts182_FullRunMandatoryGatesTests
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FullRunTimeout = TimeSpan.FromMinutes(30);

    /// <summary>when (1)+(2)/then: build sln и полный dotnet test — Exit 0.</summary>
    [Fact]
    public void FullDotNetTestRun_ExitsZero()
    {
        // (1) given: решение бэкенда собрано (предупреждения = ошибки в csproj).
        // Доработка CR-001/CR-004: ожидание замка — GenerousLockWait, бюджет
        // самой сборки — BuildTimeout.
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.SolutionFile, "--nologo", "-v", "minimal"],
            DotNetCli.GenerousLockWait,
            BuildTimeout);
        Assert.True(
            build.Succeeded,
            $"given не выполнен: dotnet build LabsApp.sln завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}).{Environment.NewLine}{build.OutputTail()}");

        // (2) when: полный прогон dotnet test. then: Exit 0. Ожидание замка —
        // GenerousLockWait (полный прогон — самая долгая команда; доработка
        // CR-001/CR-004).
        var run = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo"],
            DotNetCli.GenerousLockWait,
            FullRunTimeout);
        Assert.True(
            run.Succeeded,
            $"then не выполнен: полный dotnet test завершился с кодом {run.ExitCode} "
            + $"(timedOut={run.TimedOut}) — обязательный прогон FR-027 не зелёный."
            + $"{Environment.NewLine}{run.OutputTail()}");
    }

    /// <summary>
    /// when (3)/then: каждый из пунктов (1)–(8) признан покрытым тестами
    /// LabsApp.Tests по невакуумным комбинациям маркеров (CR-002); зелёность
    /// обеспечивает полный прогон (FullDotNetTestRun_ExitsZero).
    /// </summary>
    [Fact]
    public void MandatoryGates1To8_AreCoveredByTests_NonVacuously()
    {
        var files = BackendSuitePaths.TestSourceFiles();
        Assert.True(
            files.Count > 0,
            $"Исходники LabsApp.Tests не найдены в {BackendSuitePaths.TestsSourceDirectory}.");

        var results = BackendGateProbe.EvaluateMandatoryGates(files);
        var uncovered = results.Where(result => !result.IsCovered).ToList();

        Assert.True(
            uncovered.Count == 0,
            "Обязательные гейты FR-027 (пункты (1)–(8)) не покрыты тестами LabsApp.Tests:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                uncovered.Select(result =>
                    $"  ({result.Gate.Number}) {result.Gate.Title}:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, result.MissingRequirements.Select(item => $"    - {item}")))));
    }

    /// <summary>
    /// when (4): негативные пробы различимости — контрольный текст (корпус
    /// исходников без файлов, дающих сигнал покрытия гейта) обязан
    /// распознаваться как «гейт не покрыт» для пунктов (2), (6), (8).
    /// </summary>
    [Fact]
    public void PresenceCheck_NegativeProbes_StrippedGateTests_ReportedUncovered()
    {
        AssertGateBecomesUncoveredAfterStrippingItsFiles(
            gateNumber: 2,
            testsKind: "лимитера",
            expectedPhrase: "гейт (2) не покрыт");
        AssertGateBecomesUncoveredAfterStrippingItsFiles(
            gateNumber: 6,
            testsKind: "labs/groups/students/submissions",
            expectedPhrase: "гейт (6) не покрыт");
        AssertGateBecomesUncoveredAfterStrippingItsFiles(
            gateNumber: 8,
            testsKind: "конверта ошибок",
            expectedPhrase: "гейт (8) не покрыт");
    }

    private static void AssertGateBecomesUncoveredAfterStrippingItsFiles(
        int gateNumber, string testsKind, string expectedPhrase)
    {
        var files = BackendSuitePaths.TestSourceFiles();
        var gate = BackendGateProbe.MandatoryGates.Single(item => item.Number == gateNumber);

        // Контрольный текст: корпус без файлов, дающих сигнал покрытия гейта.
        var contributing = BackendGateProbe.FilesContributingTo(gate, files)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            contributing.Count > 0,
            $"Негативная проба для гейта ({gateNumber}) невозможна: ни один файл не даёт сигнала "
            + $"покрытия — сам гейт не покрыт (сначала закройте проверку присутствия).");
        var controlCorpus = files.Where(file => !contributing.Contains(file)).ToList();

        var results = BackendGateProbe.EvaluateMandatoryGates(controlCorpus);
        var evaluation = results.Single(result => result.Gate.Number == gateNumber);

        Assert.True(
            !evaluation.IsCovered,
            $"Негативная проба различимости не прошла: контрольный текст без тестов {testsKind} "
            + $"обязан распознаваться как «{expectedPhrase}», а проверка присутствия сигнализирует "
            + "покрытие на побочном тексте исходников. Остаточные требования, «покрытые» побочно: ["
            + string.Join("; ", evaluation.MissingRequirements) + "]");
    }
}
