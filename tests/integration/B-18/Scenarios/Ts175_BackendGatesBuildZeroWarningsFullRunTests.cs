using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// Кейс TS-175 «Бэкенд-гейты: dotnet build 0 предупреждений, dotnet test
/// exit 0» (P0, FR-027 AC «Полный прогон», NFR-002).
/// given: решение бэкенда в src/api собрано (зависимости восстановлены);
/// when:  dotnet build; dotnet test;
/// then:  dotnet build — exit 0 и 0 предупреждений (TreatWarningsAsErrors=true;
///        NFR-002: «фиксация TreatWarningsAsErrors в csproj»); dotnet test —
///        exit 0; обязательные гейты FR-027 (1)–(8) операционно покрыты
///        кейсами: (1) KDF-матрица входа — TS-040..TS-045; (2) лимитер —
///        TS-012..TS-017; (3) recovery-поток — TS-069..TS-084; (4)
///        cookie/токены — TS-051, TS-056..TS-064; (5) матрица ролей — TS-152;
///        (6) labs/groups/students/submissions — TS-097..TS-151; (7) сид —
///        TS-167; (8) конверт ошибок — TS-155..TS-158.
/// Инфраструктура — та же, что у метатестов исторической нумерации того же
/// дерева гейтов (TS-181/TS-182): хелпер DotNetCli с глобальным
/// межпроцессным замком (SrcApiBuildLock, CR-003) и разделёнными бюджетами
/// «ожидание замка / дочерний процесс» (CR-001/CR-004); тяжёлые прогоны
/// сериализуются xUnit-коллекцией «b18-backend-dotnet-cli».
/// </summary>
[Collection("b18-backend-dotnet-cli")]
public sealed class Ts175_BackendGatesBuildZeroWarningsFullRunTests
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FullRunTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Проект приложения бэкенда (для проверки фиксации NFR-002).</summary>
    private static string AppProjectFile =>
        Path.Combine(BackendSuitePaths.ApiDirectory, "LabsApp", "LabsApp.csproj");

    /// <summary>
    /// Атрибуция «пункт FR-027 → покрывающие кейсы» из then кейса TS-175 —
    /// попадает в диагностику провала, чтобы падение гейта указывало, кейсами
    /// какого диапазона это гейт операционно покрыт.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> GateCoverageCases =
        new Dictionary<int, string>
        {
            [1] = "TS-040..TS-045",
            [2] = "TS-012..TS-017",
            [3] = "TS-069..TS-084",
            [4] = "TS-051, TS-056..TS-064",
            [5] = "TS-152",
            [6] = "TS-097..TS-151",
            [7] = "TS-167",
            [8] = "TS-155..TS-158",
        };

    /// <summary>
    /// when «dotnet build» / then: exit 0 и 0 предупреждений. Нулевой
    /// предупреждений-статус гарантирован фиксацией
    /// &lt;TreatWarningsAsErrors&gt;true&lt;/TreatWarningsAsErrors&gt; в обоих
    /// csproj решения (NFR-002: предупреждение = ошибка сборки, exit 0
    /// возможен только при нуле предупреждений) и дополнительно проверяется
    /// по выводу MSBuild: ни диагностических строк «: warning XXXNNNN», ни
    /// ненулевых счётчиков «N Warning(s)» в сводке.
    /// </summary>
    [Fact]
    public void DotNetBuild_ExitsZero_WithZeroWarnings()
    {
        // given: «0 предупреждений» гарантирован конфигурацией — флаг обязан
        // быть зафиксирован в csproj приложения и csproj тестов (NFR-002).
        AssertTreatWarningsAsErrorsFixed(AppProjectFile);
        AssertTreatWarningsAsErrorsFixed(BackendSuitePaths.TestsProjectFile);

        // when: dotnet build решения (под глобальным межпроцессным замком;
        // ожидание замка — GenerousLockWait, бюджет сборки — BuildTimeout).
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.SolutionFile, "--nologo", "-v", "minimal"],
            DotNetCli.GenerousLockWait,
            BuildTimeout);

        // then: exit 0.
        Assert.True(
            build.Succeeded,
            $"then не выполнен: dotnet build LabsApp.sln завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}) — гейт NFR-002 «0 предупреждений» нарушен "
            + $"(TreatWarningsAsErrors превращает предупреждение в ошибку сборки)."
            + $"{Environment.NewLine}{build.OutputTail()}");

        // then: 0 предупреждений — по выводу MSBuild.
        var diagnosticWarnings = DiagnosticWarningLine().Matches(build.Output);
        Assert.True(
            diagnosticWarnings.Count == 0,
            "then не выполнен: в выводе dotnet build есть диагностические строки предупреждений "
            + "«: warning <code>» при требовании «0 предупреждений» (NFR-002): ["
            + string.Join("; ", diagnosticWarnings.Select(match => match.Value.Trim())) + "]"
            + $"{Environment.NewLine}{build.OutputTail(30)}");

        var nonZeroSummaries = WarningSummaryCounts()
            .Matches(build.Output)
            .Cast<Match>()
            .Where(match => match.Groups[1].Value != "0")
            .Select(match => match.Value)
            .ToList();
        Assert.True(
            nonZeroSummaries.Count == 0,
            "then не выполнен: в сводке dotnet build ненулевой счётчик предупреждений "
            + "при требовании «0 предупреждений» (NFR-002): ["
            + string.Join("; ", nonZeroSummaries) + "]"
            + $"{Environment.NewLine}{build.OutputTail(30)}");
    }

    /// <summary>
    /// when «dotnet test» / then: полный прогон LabsApp.Tests — exit 0
    /// (FR-027 AC «Полный прогон»; гейты (1)–(8) зелёные одним прогоном).
    /// given «решение собрано» обеспечивается инкрементальным build под замком
    /// перед прогоном --no-build.
    /// </summary>
    [Fact]
    public void FullDotNetTest_ExitsZero()
    {
        // given: решение бэкенда собрано (инкрементальный build; предупреждения
        // = ошибки, зелёный build = 0 предупреждений, см. NFR-002).
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.SolutionFile, "--nologo", "-v", "minimal"],
            DotNetCli.GenerousLockWait,
            BuildTimeout);
        Assert.True(
            build.Succeeded,
            $"given не выполнен: dotnet build LabsApp.sln завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}).{Environment.NewLine}{build.OutputTail()}");

        // when: полный dotnet test тестового проекта бэкенда.
        var run = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo"],
            DotNetCli.GenerousLockWait,
            FullRunTimeout);

        // then: exit 0.
        Assert.True(
            run.Succeeded,
            $"then не выполнен: полный dotnet test завершился с кодом {run.ExitCode} "
            + $"(timedOut={run.TimedOut}) — гейт «dotnet test exit 0» (FR-027 AC «Полный "
            + $"прогон», NFR-002) не зелёный.{Environment.NewLine}{run.OutputTail()}");
    }

    /// <summary>
    /// then: обязательные гейты FR-027 (1)–(8) операционно покрыты кейсами
    /// TS-040..TS-045 / TS-012..TS-017 / TS-069..TS-084 / TS-051+TS-056..TS-064
    /// / TS-152 / TS-097..TS-151 / TS-167 / TS-155..TS-158 — проверка
    /// присутствия по исходникам LabsApp.Tests невакуумными маркерами
    /// (BackendGateProbe: комментарии вырезаются, голые цифры/подстрока имени
    /// проекта доказательством не считаются); зелёность покрытия обеспечивает
    /// полный прогон (FullDotNetTest_ExitsZero). Диагностика провала указывает
    /// диапазон покрывающих кейсов каждого пункта.
    /// </summary>
    [Fact]
    public void MandatoryGates1To8_AreOperationallyCovered_ByCaseRanges()
    {
        var files = BackendSuitePaths.TestSourceFiles();
        Assert.True(
            files.Count > 0,
            $"Исходники LabsApp.Tests не найдены в {BackendSuitePaths.TestsSourceDirectory}.");

        var results = BackendGateProbe.EvaluateMandatoryGates(files);
        var uncovered = results.Where(result => !result.IsCovered).ToList();

        Assert.True(
            uncovered.Count == 0,
            "Обязательные гейты FR-027 (пункты (1)–(8)) не покрыты операционно (кейс TS-175):"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                uncovered.Select(result =>
                    $"  ({result.Gate.Number}) {result.Gate.Title} — покрывающие кейсы: "
                    + $"{GateCoverageCases[result.Gate.Number]}:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, result.MissingRequirements.Select(item => $"    - {item}")))));
    }

    /// <summary>
    /// NFR-002 «фиксация TreatWarningsAsErrors в csproj»: флаг обязан
    /// присутствовать со значением true в csproj, иначе «0 предупреждений»
    /// dotnet build ничем не гарантирован.
    /// </summary>
    private static void AssertTreatWarningsAsErrorsFixed(string csprojFile)
    {
        Assert.True(
            File.Exists(csprojFile),
            $"csproj не найден: {csprojFile} — given кейса TS-175 (решение бэкенда в src/api) не выполнен.");

        var content = File.ReadAllText(csprojFile);
        Assert.True(
            Regex.IsMatch(
                content,
                @"<TreatWarningsAsErrors>\s*true\s*</TreatWarningsAsErrors>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            $"NFR-002 нарушена: в {csprojFile} не зафиксирован "
            + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors> — требование кейса TS-175 "
            + "«dotnet build — exit 0 и 0 предупреждений» не гарантировано.");
    }

    /// <summary>
    /// Диагностическая строка предупреждения MSBuild «: warning CODE»
    /// (ключевое слово severity в диагностических строках Roslyn/MSBuild
    /// локалью не переводится; вариант «предупреждение» — на случай
    /// локализованных диагностик).
    /// </summary>
    private static Regex DiagnosticWarningLine() =>
        new(
            @":\s*(?:warning|[Пп]редупреждени\w*)\s+[A-Za-z]+\d+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Счётчик предупреждений в сводке MSBuild: «N Warning(s)» (английская
    /// локаль) либо «Предупреждений: N» (русская локаль — фактический формат
    /// хостов прогона).
    /// </summary>
    private static Regex WarningSummaryCounts() =>
        new(
            @"(?:Warning\(s\)|[Пп]редупреждени\w*)\s*:?\s*(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
