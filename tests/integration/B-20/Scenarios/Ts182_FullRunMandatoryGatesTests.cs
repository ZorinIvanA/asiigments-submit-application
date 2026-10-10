using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// TS-182 «Бэкенд-тесты: полный прогон покрывает обязательные гейты (1)–(8)
/// (границы собственной зоны)» (P0, FR-027 AC «Полный прогон»; переиздан
/// a-048 по CR-003).
/// given: существуют решение src/api/LabsApp.sln и тестовый проект
///        src/api/LabsApp.Tests; метатесты — в зоне собственного батча
///        tests/integration/B-20 (те же собственные хелперы, что и в TS-181:
///        DotNetCli, BackendSuitePaths, BackendGateProbe); зона B-20 не входит
///        в проверяемое решение; файлы чужих тестовых зон буквой кейса не
///        используются;
/// when:  (1) dotnet build src/api/LabsApp.sln; (2) полный dotnet test
///        src/api/LabsApp.Tests; (3) анализ исходников LabsApp.Tests на
///        покрытие пунктов: (1) KDF-матрица; (2) лимитер (5/6, отказ без
///        метки, скольжение, потолок+overflow, независимость ключей);
///        (3) recovery-поток (request/confirm/reset-password); (4)
///        cookie/токены; (5) матрица ролей; (6) labs/groups/students/
///        submissions happy+404+409+валидация+нормализация page; (7) сид
///        идемпотентность; (8) конверт ошибок 400/404/500;
/// then:  Build и полный dotnet test — exit 0; тесты, соответствующие каждому
///        из пунктов (1)–(8), присутствуют и зелёные. Межзонная
///        параллельность сборок в букву кейса НЕ входит и его зоной не
///        проверяется (переиздан a-048 по CR-003).
/// Проверка присутствия — невакуумная: маркеры оцениваются в коде без
/// комментариев, голые цифры/подстроки имени проекта доказательством не
/// считаются; различимость подтверждается негативными пробами (контрольный
/// корпус без файлов гейта обязан распознаваться как «гейт не покрыт»).
/// </summary>
[Collection("b20-backend-dotnet-cli")]
public sealed class Ts182_FullRunMandatoryGatesTests
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FullRunTimeout = TimeSpan.FromMinutes(30);

    /// <summary>when (1)+(2)/then: build sln и полный dotnet test — exit 0.</summary>
    [Fact]
    public void FullDotNetTestRun_ExitsZero()
    {
        // (1) given: решение бэкенда собрано (предупреждения = ошибки в csproj).
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.SolutionFile, "--nologo", "-v", "minimal"],
            BuildTimeout);
        Assert.True(
            build.Succeeded,
            $"given не выполнен: dotnet build LabsApp.sln завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}).{Environment.NewLine}{build.OutputTail()}");

        // (2) when: полный прогон dotnet test. then: Exit 0.
        var run = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo"],
            FullRunTimeout);
        Assert.True(
            run.Succeeded,
            $"then не выполнен: полный dotnet test завершился с кодом {run.ExitCode} "
            + $"(timedOut={run.TimedOut}) — обязательный прогон FR-027 не зелёный."
            + $"{Environment.NewLine}{run.OutputTail()}");
    }

    /// <summary>
    /// when (3)/then: каждый из пунктов (1)–(8) признан покрытым тестами
    /// LabsApp.Tests по невакуумным комбинациям маркеров; зелёность
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
    /// Негативные пробы различимости — контрольный текст (корпус исходников
    /// без файлов, дающих сигнал покрытия гейта) обязан распознаваться как
    /// «гейт не покрыт» для пунктов (2), (6), (8).
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
