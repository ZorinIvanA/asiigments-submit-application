using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// Кейс TS-146 (текущая нумерация раунда; историческая нумерация — TS-181,
/// переиздан a-034) «Unit-гейты: dotnet test зелёный, KDF-матрица входа
/// утверждена» — KDF-половина кейса: «в тестах KDF-матрицы утверждается
/// Δkdf=1 для обеих 429-веток (известный и неизвестный логин) и сценарий
/// «успех на заблокированном ключе» (FR-027 AC «Гейт KDF-матрицы»)».
/// (P0, FR-027 AC «Гейт KDF-матрицы», FR-007 шаги (4)–(6)).
/// given: метатесты — в зоне tests/integration/B-18 (дочерний dotnet — только
///        через Infrastructure/DotNetCli.cs с глобальным межпроцессным замком
///        CR-003 и диагностикой таймаута результатом CR-004);
/// when:  (1) dotnet build LabsApp.Tests через замкнутый хелпер; (2) поиск
///        тестов KDF-матрицы — файлы с «/auth/login» и «kdf» одновременно;
///        (3) проверка шести веток БЕЗ схлопывания одинаковых предикатов
///        (CR-001): в одном тест-методе/блоке совместно маркер статуса
///        (200/401/429 либо Ok/Unauthorized/TooManyRequests), отличитель
///        ветки (для известных веток — искомый вне «unknown»/«неизвестн»,
///        доработка CR-001) и маркер утверждения Δkdf=1 (третий конъюнкт,
///        доработка CR-002); у двух 429-веток отличители различны
///        (known/известн против unknown/неизвестн/ghost); у «успеха на
///        заблокированном ключе» — успех + исчерпанный ключ/окно лимитера;
///        (4) негативные пробы различимости в ОБЕ стороны: (а) контрольный
///        текст, где вторая 429-ветка упомянута только в комментарии, обязан
///        дать «вторая 429-ветка не покрыта»; (б) контрольный текст только
///        с unknown-429-блоком обязан дать «известная 429-ветка не покрыта»;
///        (в) контрольный корпус из двух «файлов», где маркер Δkdf есть
///        только в заголовке второго файла, обязан оставить известную
///        429-ветку непокрытой (склейка файлов, доработка CR-002); (г) блок
///        неизвестного логина с «не существует» и вызовом *Exists* обязан
///        оставить известную ветку непокрытой (сужение отличителя,
///        доработка CR-003); (5) прогон dotnet test --no-build --filter
///        с защитой от ложной зелени через --list-tests;
/// then:  тесты KDF-матрицы найдены, Δkdf=1 утверждается во всех шести
///        ветках (включая ОБЕ 429-ветки по отдельности), фильтрованный прогон
///        зелёный, негативные пробы (в обе стороны) подтверждают
///        различимость; сборки ДЕРЖАТЕЛЕЙ замка B-20/B-22 не пересекаются
///        на obj/ (замок CR-003; зона B-21 dotnet-прогоны над src/api ведёт
///        без замка — некоординированный участник, доработка CR-001: от неё
///        защищает только сериализация зон диспетчеризацией), таймауты
///        диагностируются результатом (CR-004), а бюджет ожидания замка
///        отделён от бюджета дочернего процесса (доработка CR-001/CR-004).
/// </summary>
[Collection("b18-backend-dotnet-cli")]
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
    /// when (3)/then: Δkdf=1 утверждается во всех шести ветках; предикаты
    /// веток не схлопнуты — у двух 429-веток отличители различны (CR-001).
    /// </summary>
    [Fact]
    public void GateTests_AssertDeltaKdfOne_ForEveryDistinguishableBranch()
    {
        // given: найденные файлы гейта KDF-матрицы (тексты читаются ПОФАЙЛОВО:
        // разбиение на блоки идёт по каждому файлу отдельно, доработка CR-002 —
        // конкатенация склеивала хвост последнего тест-метода файла N
        // с заголовком файла N+1).
        var gateFiles = BackendGateProbe.FindKdfMatrixGateFiles();
        Assert.True(
            gateFiles.Count > 0,
            "Тесты KDF-матрицы не найдены — проверка веток Δkdf=1 невозможна (гейт (1) FR-027).");
        var gateTexts = gateFiles.Select(File.ReadAllText).ToList();

        // CR-001: наборы маркеров-отличителей двух 429-веток не идентичны.
        var known429 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "429-known");
        var unknown429 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "429-unknown");
        Assert.NotEqual(
            known429.DistinguisherAnyOf, unknown429.DistinguisherAnyOf);

        // Δkdf=1: маркеры снимков счётчика KDF присутствуют в тестах гейта
        // (глобальная any-of-проверка по всему корпусу).
        Assert.True(
            BackendGateProbe.DeltaKdfAsserted(BackendGateProbe.UnionText(gateFiles)),
            "В тестах KDF-матрицы входа не распознано утверждение Δkdf=1 "
            + "(снимки счётчика KDF до/после запроса; ищутся: "
            + string.Join(", ", BackendGateProbe.DeltaKdfMarkers) + ").");

        // when: пометодная проверка шести веток (без схлопывания предикатов,
        // блоки — пофайлово, доработка CR-002).
        var missing = BackendGateProbe.MissingKdfBranches(gateTexts);

        // then: каждая из шести веток распознана в отдельном тест-методе/блоке.
        Assert.True(
            missing.Count == 0,
            "В тестах KDF-матрицы входа не распознаны ветки с Δkdf=1 "
            + "(в пределах одного тест-метода нужны маркер статуса, отличитель ветки "
            + "и маркер утверждения Δkdf=1; комментарии, вложенность отличителей "
            + "(CR-001) и одиночные универсальные токены не считаются):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, gateFiles.Select(file => $"  файл: {file}"))
            + Environment.NewLine
            + "Непокрытые ветки:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// when (4а): негативная проба различимости, встроенная в метатест — на
    /// контрольном тексте, где покрыта только одна 429-ветка (вторая
    /// упомянута лишь в комментарии), проверка обязана вернуть
    /// «вторая 429-ветка не покрыта».
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

    /// <summary>
    /// when (4б): ВСТРЕЧНАЯ негативная проба различимости (доработка CR-001) —
    /// контрольный текст, где покрыта ТОЛЬКО ветка «429 (неизвестный логин)»
    /// (блок с «Unknown», содержащим вложенное «known»), обязан дать
    /// «429 (известный логин…) не покрыта». Без маскирования вложенности
    /// отличителей (подстрока «known»⊂«unknown», «известн»⊂«неизвестн») блок
    /// неизвестного логина «доказывал» бы и известную 429-ветку, и удаление
    /// теста Login_BlockedKnownUser_… осталось бы незамеченным.
    /// </summary>
    [Fact]
    public void BranchCheck_NegativeProbe_UnknownOnly429Block_Known429ReportedUncovered()
    {
        const string controlText = """
            // Контрольный текст: единственный тест-блок — ветка «неизвестный логин»
            // (преамбула файла блоком не считается, как и в реальных файлах гейта).
            public sealed class LoginGateHarness
            {
                [Fact]
                public async Task Login_BlockedUnknownUser_Returns429_AfterExactlyOneKdf()
                {
                    var before = counter.KdfSnapshot();
                    var response = await client.PostAsync("/auth/login", Json(new { login = "ghost", password = "whatever1!" }));
                    Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                    Assert.Equal(429, (int)response.StatusCode);
                    var after = counter.KdfSnapshot();
                    Assert.Equal(before.TotalDelta + 1, after.TotalDelta);
                }
            }
            """;

        var missing = BackendGateProbe.MissingKdfBranches(controlText);

        Assert.True(
            missing.Contains(known429BranchTitle) && !missing.Contains(unknown429BranchTitle),
            "Встречная негативная проба различимости не прошла: на контрольном тексте, где покрыта "
            + "только «" + unknown429BranchTitle + "» (блок содержит «Unknown» с вложенным «known»), "
            + "проверка обязана вернуть «" + known429BranchTitle + " не покрыта» — вложенность "
            + "отличителей маскируется, блок неизвестного логина не доказывает известную ветку. "
            + "Фактический список непокрытых веток: [" + string.Join("; ", missing) + "]");
    }

    private const string known429BranchTitle = "429 (известный логин, заблокированный ключ)";
    private const string unknown429BranchTitle = "429 (неизвестный логин, заблокированный ключ)";

    /// <summary>
    /// when (4в): негативная проба СКЛЕЙКИ ФАЙЛОВ (доработка CR-002) —
    /// контрольный корпус из двух «файлов», где маркер утверждения Δkdf есть
    /// только в ЗАГОЛОВКЕ второго файла (до первого [Fact], как у реального
    /// Hosting/TestSession.cs без единого тест-атрибута). При конкатенации
    /// исходных текстов хвост последнего тест-метода первого файла склеивался
    /// с заголовком второго, и известная 429-ветка «покрывалась» маркерами из
    /// РАЗНЫХ файлов; при пофайловом делении на блоки (SplitIntoMethodBlocks
    /// по файлам, доработка CR-002) ветка обязана остаться непокрытой, а
    /// собственный блок второго файла — распознанным.
    /// </summary>
    [Fact]
    public void BranchCheck_NegativeProbe_DeltaMarkerOnlyInHeaderOfSecondFile_NoFalsePositive()
    {
        const string fileOne = """
            public sealed class GluedGateHarness
            {
                // Полный блок 429-ветки известного логина, но БЕЗ маркера Δkdf:
                // маркер снимков счётчика «живёт» только в заголовке второго файла.
                [Fact]
                public async Task Login_429_KnownLogin_BlockedKey_NoDeltaHere()
                {
                    var response = await client.PostAsync("/auth/login", Json(new { login = KnownLogin, password = "nope123!" }));
                    Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                    Assert.Equal(429, (int)response.StatusCode);
                }
            }
            """;

        const string fileTwo = """
            // Хелперы снимков счётчика KDF: counter.KdfSnapshot()/TotalDelta —
            // до первого [Fact], поэтому блоком тест-метода не считаются.
            public static class KdfSnapshotHelpers
            {
                public static long TotalDelta(long before, long after) => after - before;
            }

            public sealed class SecondFileGateTests
            {
                [Fact]
                public async Task Login_Unknown_401_WithDelta()
                {
                    var before = counter.KdfSnapshot();
                    var response = await client.PostAsync("/auth/login", Json(new { login = "ghost", password = "whatever1!" }));
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                    var after = counter.KdfSnapshot();
                    Assert.Equal(before.TotalDelta + 1, after.TotalDelta);
                }
            }
            """;

        // when: ветки оцениваются по корпусу из двух файлов (по-файловые блоки).
        var missing = BackendGateProbe.MissingKdfBranches([fileOne, fileTwo]);

        var known429 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "429-known");
        var unknown401 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "401-unknown");

        // then (а): известная 429-ветка обязана остаться непокрытой — её блок
        // в первом файле не содержит собственного маркера Δkdf, а маркер
        // из заголовка второго файла к блоку не приклеивается.
        Assert.True(
            missing.Contains(known429.Title),
            "Негативная проба склейки файлов не прошла: маркер Δkdf есть только в заголовке "
            + "второго файла корпуса, но известная 429-ветка признана покрытой — разбиение "
            + "на блоки склеивает хвост последнего тест-метода первого файла с заголовком "
            + "второго (доработка CR-002). Фактический список непокрытых веток: ["
            + string.Join("; ", missing) + "]");

        // then (б): собственный блок второго файла обязан распознаваться —
        // пофайловое деление не теряет блоки файлов корпуса.
        Assert.True(
            !missing.Contains(unknown401.Title),
            "Негативная проба склейки файлов не прошла: блок 401-ветки неизвестного логина "
            + "во втором файле корпуса не распознан — пофайловое разбиение теряет блоки "
            + "(доработка CR-002). Фактический список непокрытых веток: ["
            + string.Join("; ", missing) + "]");
    }

    /// <summary>
    /// when (4г): негативная проба сужения отличителя «известный логин»
    /// (доработка CR-003) — контрольный блок ветки НЕИЗВЕСТНОГО логина,
    /// содержащий «не существует» и вызов идентификатора семейства *Exists*
    /// (ExistsNameCi/groupExists — обычные имена кодовой базы, контракт
    /// IF-015): прежде токены «существ» (⊂ «не существует») и «exists»
    /// (⊂ «Exists…») совпадали с этими вхождениями, и блок неизвестного
    /// логина «доказывал» известную 401-ветку — ложноположительное покрытие.
    /// После сужения отличителя блок обязан оставить известную 401-ветку
    /// непокрытой, оставаясь покрытием собственной неизвестной ветки.
    /// </summary>
    [Fact]
    public void BranchCheck_NegativeProbe_UnknownBlockWithExistsIdentifiers_Known401ReportedUncovered()
    {
        const string controlText = """
            // Контрольный текст: единственный тест-блок — ветка «неизвестный логин»,
            // в коде блока — «не существует» и вызов *Exists* (не комментарий).
            public sealed class LoginGateHarness
            {
                [Fact]
                public async Task Login_UnknownUser_Returns401_AfterExactlyOneKdf()
                {
                    if (users.GetByLogin("ghost") is null)
                    {
                        diagnostics.Add("пользователь не существует");
                    }

                    var before = counter.KdfSnapshot();
                    var response = await client.PostAsync("/auth/login", Json(new { login = "ghost", password = "whatever1!" }));
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                    Assert.Equal(401, (int)response.StatusCode);
                    var after = counter.KdfSnapshot();
                    Assert.Equal(before.TotalDelta + 1, after.TotalDelta);
                    Assert.False(groups.ExistsNameCi("ghost-group"));
                }
            }
            """;

        var missing = BackendGateProbe.MissingKdfBranches(controlText);

        var known401 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "401-known-wrong");
        var unknown401 = BackendGateProbe.KdfMatrixBranches.Single(branch => branch.Id == "401-unknown");

        Assert.True(
            missing.Contains(known401.Title) && !missing.Contains(unknown401.Title),
            "Негативная проба сужения отличителя не прошла: блок ветки НЕИЗВЕСТНОГО логина "
            + "(содержащий «не существует» и вызов ExistsNameCi) не должен доказывать "
            + "известную 401-ветку — отличитель «известный логин» сужён до контекстных "
            + "токенов (доработка CR-003). Фактический список непокрытых веток: ["
            + string.Join("; ", missing) + "]");
    }

    /// <summary>
    /// when (1)+(5)/then: сборка LabsApp.Tests через замкнутый хелпер;
    /// фильтрованный прогон найденных тестов гейта зелёный, с защитой от
    /// ложной зелени (предварительный --list-tests обязан выбрать тест).
    /// Доработка CR-001/CR-004: бюджет ожидания замка (GenerousLockWait —
    /// держатели B-20/B-22 исполняют над src/api вплоть до полного
    /// dotnet test) отделён от бюджета дочернего процесса.
    /// </summary>
    [Fact]
    public void GateTests_GateRun_IsGreen()
    {
        // (1) given: тестовый проект собран (dotnet build; TreatWarningsAsErrors
        // в csproj превращает предупреждения в ненулевой код выхода).
        var build = DotNetCli.Run(
            ["build", BackendSuitePaths.TestsProjectFile, "--nologo", "-v", "minimal"],
            DotNetCli.GenerousLockWait,
            BuildTimeout);
        Assert.True(
            build.Succeeded,
            $"given не выполнен: dotnet build LabsApp.Tests завершился с кодом {build.ExitCode} "
            + $"(timedOut={build.TimedOut}).{Environment.NewLine}{build.OutputTail()}");

        // (5) when: поиск тестов гейта и фильтр по их классам.
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
            DotNetCli.GenerousLockWait,
            TestRunTimeout);
        Assert.True(
            listing.Succeeded && ContainsAnyClassName(listing.Output, classNames),
            $"Фильтр гейта не выбирает ни одного теста (фильтр: {filter})."
            + $"{Environment.NewLine}{listing.OutputTail()}");

        var run = DotNetCli.Run(
            ["test", BackendSuitePaths.TestsProjectFile, "--no-build", "--nologo", "--filter", filter],
            DotNetCli.GenerousLockWait,
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
