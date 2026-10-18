using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// TS-181 «Бэкенд-тесты: гейт KDF-матрицы входа присутствует и зелёный
/// (границы собственной зоны)» (P0, FR-027 AC «Гейт KDF-матрицы»; переиздан
/// a-048 по CR-003).
/// given: существуют решение src/api/LabsApp.sln и тестовый проект
///        src/api/LabsApp.Tests; метатесты кейса — в зоне собственного батча
///        tests/integration/B-20 с собственными хелперами
///        Infrastructure/DotNetCli.cs (дочерние dotnet-команды),
///        Infrastructure/BackendSuitePaths.cs (пути LabsApp.sln /
///        LabsApp.Tests), Infrastructure/BackendGateProbe.cs (разбор
///        исходников гейтов); зона B-20 НЕ включена в src/api/LabsApp.sln
///        (рекурсии прогона нет); файлы чужих тестовых зон буквой кейса не
///        используются и её зоной не правятся;
/// when:  (1) dotnet build src/api/LabsApp.sln; (2) поиск в исходниках
///        src/api/LabsApp.Tests (без bin/obj) тестов KDF-матрицы входа —
///        файлов, содержащих одновременно маркеры POST /auth/login и Δkdf
///        (снимки счётчика auth_kdf_operations_total до/после); (3) их прогон
///        dotnet test с фильтром;
/// then:  Build — exit 0; тест(ы) KDF-матрицы найдены и зелёные; в них
///        утверждается Δkdf=1 для веток: 200 (известный+верный), 401
///        (известный+неверный), 401 (неизвестный), 429 (известный,
///        заблокированный ключ), 429 (неизвестный, заблокированный ключ);
///        плюс сценарий «успех на заблокированном ключе» — 200. Кейс
///        утверждает ТОЛЬКО гейты FR-027 для src/api/LabsApp.Tests:
///        межзонная изоляция параллельных dotnet build/test других тестовых
///        зон НЕ входит в букву кейса и его зоной не проверяется (замок
///        DotNetCli — только механизм детерминизма собственного прогона).
/// Защита мета-проверок от «вакуумной зелени»: ветка засчитывается только в
/// пределах одного тест-метода (совместно статус и отличитель), комментарии
/// доказательством не считаются — различимость проверяется встроенной
/// негативной пробой на контрольном тексте.
/// </summary>
[Collection("b20-backend-dotnet-cli")]
public sealed class Ts181_LoginKdfMatrixGateTests
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TestRunTimeout = TimeSpan.FromMinutes(15);

    /// <summary>when (2): тесты KDF-матрицы входа найдены в src/api/LabsApp.Tests.</summary>
    [Fact]
    public void GateTests_AreFoundInLabsAppTests()
    {
        var gateFiles = BackendGateProbe.FindKdfMatrixGateFiles();

        Assert.True(
            gateFiles.Count > 0,
            $"Гейт (1) FR-027 не покрыт: в {BackendSuitePaths.TestsSourceDirectory} не найдено ни одного файла, "
            + $"содержащего «{BackendGateProbe.LoginPathMarker}» и «kdf» одновременно "
            + "(тесты KDF-матрицы входа для POST /auth/login отсутствуют).");
    }

    /// <summary>
    /// then: Δkdf=1 утверждается во всех шести ветках; предикаты веток не
    /// схлопнуты — у двух 429-веток отличители различны.
    /// </summary>
    [Fact]
    public void GateTests_AssertDeltaKdfOne_ForEveryDistinguishableBranch()
    {
        // given: найденные файлы гейта KDF-матрицы.
        var gateFiles = BackendGateProbe.FindKdfMatrixGateFiles();
        Assert.True(
            gateFiles.Count > 0,
            "Тесты KDF-матрицы не найдены — проверка веток Δkdf=1 невозможна (гейт (1) FR-027).");
        var gateText = BackendGateProbe.UnionText(gateFiles);

        // Наборы маркеров-отличителей двух 429-веток не идентичны.
        var known429 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "429-known");
        var unknown429 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "429-unknown");
        Assert.NotEqual(
            known429.DistinguisherAnyOf, unknown429.DistinguisherAnyOf);

        // Δkdf=1: маркеры снимков счётчика KDF присутствуют в тестах гейта.
        Assert.True(
            BackendGateProbe.DeltaKdfAsserted(gateText),
            "В тестах KDF-матрицы входа не распознано утверждение Δkdf=1 "
            + "(снимки счётчика auth_kdf_operations_total до/после запроса; ищутся: "
            + string.Join(", ", BackendGateProbe.DeltaKdfMarkers) + ").");

        // when: пометодная проверка шести веток (без схлопывания предикатов).
        var missing = BackendGateProbe.MissingKdfBranches(gateText);

        // then: каждая из шести веток распознана в отдельном тест-методе/блоке.
        Assert.True(
            missing.Count == 0,
            "В тестах KDF-матрицы входа не распознаны ветки с Δkdf=1 "
            + "(в пределах одного тест-метода нужны маркер статуса и отличитель ветки; "
            + "комментарии и одиночные универсальные токены не считаются):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, gateFiles.Select(file => $"  файл: {file}"))
            + Environment.NewLine
            + "Непокрытые ветки:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// Негативная проба различимости, встроенная в метатест: на контрольном
    /// тексте, где покрыта только одна 429-ветка (вторая упомянута лишь в
    /// комментарии), проверка обязана вернуть «вторая 429-ветка не покрыта».
    /// </summary>
    [Fact]
    public void BranchCheck_NegativeProbe_CommentOnlySecond429Branch_IsReportedUncovered()
    {
        const string controlText = """
            // Гейт KDF-матрицы входа (FR-027): вторая 429-ветка (неизвестный логин)
            // должна быть покрыта ниже — пока она упомянута только здесь, в комментарии.
            [Fact]
            public async Task Login_429_KnownLogin_BlockedKey_DeltaKdfIsOne()
            {
                var kdfBefore = counter.Snapshot();
                var response = await client.PostAsync("/auth/login", Json(new { login = "teacher", password = "nope123!" }));
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.Equal(429, (int)response.StatusCode);
                Assert.Equal(kdfBefore + 1, counter.Snapshot());
            }

            // TODO: добавить 429 для неизвестного логина (ghost) — Δkdf=1.
            """;

        var missing = BackendGateProbe.MissingKdfBranches(controlText);

        Assert.True(
            missing.Contains(unknown429BranchTitle) && !missing.Contains(known429BranchTitle),
            "Негативная проба различимости не прошла: на контрольном тексте, где покрыта только "
            + "«" + known429BranchTitle + "» (вторая 429-ветка упомянута лишь в комментарии), проверка "
            + "обязана вернуть «вторая 429-ветка не покрыта». Фактический список непокрытых веток: ["
            + string.Join("; ", missing) + "]");
    }

    private const string known429BranchTitle = "429 (известный логин, заблокированный ключ)";
    private const string unknown429BranchTitle = "429 (неизвестный логин, заблокированный ключ)";

    /// <summary>
    /// when (1)+(3)/then: dotnet build src/api/LabsApp.sln — exit 0;
    /// фильтрованный прогон найденных тестов гейта зелёный, с защитой от
    /// ложной зелени (предварительный --list-tests обязан выбрать тест).
    /// </summary>
    [Fact]
    public void GateTests_GateRun_IsGreen()
    {
        // (1) given: решение бэкенда собрано (TreatWarningsAsErrors в csproj
        // превращает предупреждения в ненулевой код выхода).
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.SolutionFile, "--nologo", "-v", "minimal"],
            BuildTimeout);
        Assert.True(
            build.Succeeded,
            $"given не выполнен: dotnet build src/api/LabsApp.sln завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}).{Environment.NewLine}{build.OutputTail()}");

        // (3) when: поиск тестов гейта и фильтр по их классам.
        var gateFiles = BackendGateProbe.FindKdfMatrixGateFiles();
        Assert.True(
            gateFiles.Count > 0,
            "Тесты KDF-матрицы не найдены — прогон гейта невозможен (гейт (1) FR-027).");
        var classNames = BackendGateProbe.ExtractTestClassNames(gateFiles);
        Assert.True(
            classNames.Count > 0,
            "В файлах KDF-матрицы не найдено ни одного класса тестов: "
            + string.Join(", ", gateFiles));
        var filter = BackendGateProbe.KdfGateTestFilter(classNames);

        // Защита от ложной зелени: фильтр обязан выбирать хотя бы один тест.
        var listing = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo", "--list-tests", "--filter", filter],
            TestRunTimeout);
        Assert.True(
            listing.Succeeded && ContainsAnyClassName(listing.Output, classNames),
            $"Фильтр гейта не выбирает ни одного теста (фильтр: {filter})."
            + $"{Environment.NewLine}{listing.OutputTail()}");

        var run = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo", "--filter", filter],
            TestRunTimeout);
        Assert.True(
            run.Succeeded,
            $"then не выполнен: прогон KDF-матрицы (фильтр: {filter}) завершился с кодом "
            + $"{run.ExitCode} (timedOut={run.TimedOut}) — тесты гейта не зелёные (FR-027)."
            + $"{Environment.NewLine}{run.OutputTail()}");
    }

    private static bool ContainsAnyClassName(string output, IReadOnlyList<string> classNames) =>
        classNames.Any(name => output.Contains(name, StringComparison.Ordinal));
}
