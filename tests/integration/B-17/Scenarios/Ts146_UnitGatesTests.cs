using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-146 (FR-027, P0): unit-гейты — dotnet test зелёный, KDF-матрица входа
/// утверждена.
///
/// given: тестовый проект бэкенда (src/api/LabsApp.Tests) собран.
/// when:  dotnet test; инспекция тестов KDF-матрицы входа.
/// then:  exit 0 (FR-027 AC «Полный прогон»); покрытие затрагивает гейты (1)–(8)
///        FR-027; в тестах KDF-матрицы утверждается Δkdf=1 для обеих 429-веток
///        (известный и неизвестный логин) и сценарий «успех на заблокированном
///        ключе» (FR-027 AC «Гейт KDF-матрицы»).
///
/// Замечания по запуску: дочерний dotnet test сам собирает тест-проект
/// (given «собран»); прогон полный (~800 тестов), тайм-аут 20 минут — образец
/// зоны B-19 (TS-149). Сборка зоны исполняется последовательно
/// (xunit.runner.json: parallelizeTestCollections=false) — конкуренции за
/// obj/bin src/api с WebApplicationFactory-тестами батча нет.
/// </summary>
public sealed class Ts146_UnitGatesTests
{
    private const int DotnetTestTimeoutMs = 1_200_000;

    /// <summary>
    /// TS-146 / when «dotnet test»: полный прогон тест-проекта бэкенда;
    /// then: exit 0.
    /// </summary>
    [Fact]
    public async Task DotnetTest_BackendUnitTests_ExitsZero()
    {
        // given: тестовый проект бэкенда существует (сборку выполняет сам dotnet test).
        Assert.True(
            File.Exists(B17RepoPaths.ApiTestsProjectFile),
            $"Предусловие кейса: не найден тест-проект бэкенда {B17RepoPaths.ApiTestsProjectFile}.");

        // when: dotnet test тест-проекта бэкенда (зона tests/integration/B-17
        // в решение src/api не входит — рекурсии прогона нет).
        var result = await B17DotNetCli.RunAsync(
            ["test", B17RepoPaths.ApiTestsProjectFile, "--nologo", "-v", "q"],
            B17RepoPaths.RepositoryRoot,
            DotnetTestTimeoutMs);

        // then: exit 0 (FR-027 AC «Полный прогон»).
        Assert.True(
            result.ExitCode == 0,
            $"Ожидался код 0 у dotnet test (FR-027), фактически {result.ExitCode}. "
            + $"Вывод:{Environment.NewLine}{result.OutputTail()}");
    }

    /// <summary>
    /// TS-146 / when «инспекция тестов KDF-матрицы входа»: в тест-проекте
    /// утверждается Δkdf=1 для обеих 429-веток (известный и неизвестный логин)
    /// и присутствует сценарий «успех на заблокированном ключе»
    /// (FR-027 AC «Гейт KDF-матрицы», SEC-001).
    /// </summary>
    [Fact]
    public void KdfMatrixTests_AssertDeltaKdfOne_OnBoth429Branches_AndSuccessOnBlockedKey()
    {
        // given: исходник эндпоинт-тестов auth с KDF-матрицей входа (SEC-001).
        var source = ReadTestSource(B17RepoPaths.TestSource("Auth", "AuthEndpointTests.cs"));

        // then: 429-ветка ИЗВЕСТНОГО логина — «429 (известный, заблокированный
        // ключ)» с Δkdf(login)=1.
        var known429 = ExtractTestMethodBody(
            source, "Login_BlockedKnownUser_Returns429_AfterExactlyOneKdf_NoMark");
        Assert.Contains("HttpStatusCode.TooManyRequests", known429, StringComparison.Ordinal);
        Assert.Matches(OneCallerDeltaPattern("Login"), known429);

        // then: 429-ветка НЕИЗВЕСТНОГО логина — «429 (неизвестный, заблокированный
        // ключ)» с Δkdf=1 (эталонная деривация, метка reference).
        var unknown429 = ExtractTestMethodBody(
            source, "Login_BlockedUnknownUser_Returns429_AfterExactlyOneKdf");
        Assert.Contains("HttpStatusCode.TooManyRequests", unknown429, StringComparison.Ordinal);
        Assert.Matches(OneCallerDeltaPattern("Reference"), unknown429);

        // then: сценарий «успех на заблокированном ключе» — 200, Δkdf=1.
        var successOnBlockedKey = ExtractTestMethodBody(
            source, "Login_CorrectPasswordOnBlockedKey_Succeeds_WritesNoMark");
        Assert.Contains("HttpStatusCode.OK", successOnBlockedKey, StringComparison.Ordinal);
        Assert.Matches(
            @"Assert\.Equal\(1,\s*AuthEndpointHarness\.TotalDelta\(before,",
            successOnBlockedKey);
    }

    /// <summary>
    /// TS-146 / then «покрытие затрагивает гейты (1)–(8)»: для каждого гейта
    /// FR-027 в тест-проекте бэкенда существует покрывающий его тест.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void UnitGates_CoverFr027Gate(int gate)
    {
        switch (gate)
        {
            case 1:
                // (1) SEC-001 KDF-матрица входа: ветки 200 (известный+верный),
                // 401 (известный+неверный), 401 (неизвестный), 429 (известный,
                // заблокированный ключ), 429 (неизвестный, заблокированный ключ)
                // плюс «успех на заблокированном ключе».
                var auth = ReadTestSource(B17RepoPaths.TestSource("Auth", "AuthEndpointTests.cs"));
                Assert.Contains("AuthLoginEndpointTests", auth, StringComparison.Ordinal);
                foreach (var method in new[]
                         {
                             "Login_KnownUserCorrectPassword_Returns200MeDtoCookies_SingleKdf",
                             "Login_WrongPassword_KnownUser_Returns401_SingleKdf",
                             "Login_UnknownUser_ReturnsSame401_ReferenceKdf",
                             "Login_BlockedKnownUser_Returns429_AfterExactlyOneKdf_NoMark",
                             "Login_BlockedUnknownUser_Returns429_AfterExactlyOneKdf",
                             "Login_CorrectPasswordOnBlockedKey_Succeeds_WritesNoMark",
                         })
                {
                    Assert.Contains(method, auth, StringComparison.Ordinal);
                }

                break;
            case 2:
                // (2) Лимитер: 5-я попытка допускается/6-я блокируется, отказ
                // метку не пишет, скольжение окна (метка ровно windowMs назад —
                // вне окна), потолок MaxTrackedKeys + overflow, независимость
                // ключей.
                var limiter = ReadTestSource(B17RepoPaths.TestSource(
                    "Auth", "RateLimiting", "SlidingWindowLimiterTests.cs"));
                foreach (var method in new[]
                         {
                             "TryAcquire_FirstLimitAllowed_RefusalsDoNotExtendWindow",
                             "TryAcquire_MarkExactlyWindowMsOld_IsOutsideWindow",
                             "Keys_AreIndependent",
                             "Ceiling_NewKeysBeyondMaxTrackedKeys_ShareOverflowBucket",
                         })
                {
                    Assert.Contains(method, limiter, StringComparison.Ordinal);
                }

                Assert.True(
                    File.Exists(B17RepoPaths.TestSource("Auth", "RateLimiting", "RateLimitStoreTests.cs")),
                    "Гейт (2) FR-027: не найден тест хранилища лимитера RateLimitStoreTests.cs.");
                Assert.True(
                    File.Exists(B17RepoPaths.TestSource("Auth", "RateLimiting", "ApplicationRateLimitersTests.cs")),
                    "Гейт (2) FR-027: не найден тест конфигурации лимитеров ApplicationRateLimitersTests.cs.");
                break;
            case 3:
                // (3) Recovery-поток: request (всегда 200, переотправка гасит
                // прежний код, 3/час), confirm (верный/неверный/5-я попытка/
                // неизвестный email), reset-password (успех, ветки отказа,
                // отзыв refresh).
                var recovery = ReadTestSource(B17RepoPaths.TestSource("Recovery", "RecoveryEndpointTests.cs"));
                foreach (var method in new[]
                         {
                             "Request_ExistingEmail_200EmptyBody_SingleLiveCode_TtlTenMinutes_EmailOnce_ZeroKdf",
                             "Request_NonexistentEmail_200EmptyBody_EmailSentSameWay",
                             "Request_Resend_ExtinguishesPreviousCode_OldRejected_NewConfirmed",
                             "Request_FourthAttemptExistingEmail_429_UnifiedMessage",
                             "Request_FourthAttemptNonexistentEmail_429_UnifiedMessage",
                             "Confirm_CorrectCode_Trimmed_ReturnsResetToken_LiveFifteenMinutes_CodeConsumed",
                             "Confirm_WrongCode_IncrementsAttempts_UnifiedText",
                             "Confirm_FifthWrongAttempt_AnnullsCode_CorrectCodeThenRejected",
                             "Confirm_UnknownEmail_UnifiedText",
                             "Reset_FullFlow_204_OldPasswordFails_NewWorks_TokensConsumed_RefreshRevoked_DeltaKdfOne",
                             "Reset_UnknownToken_400ResetLinkInvalid_ZeroKdf_PasswordKept",
                         })
                {
                    Assert.Contains(method, recovery, StringComparison.Ordinal);
                }

                break;
            case 4:
                // (4) Cookie/токены: флаги cookie, refresh 204/401, logout отзыв.
                var authCookies = ReadTestSource(B17RepoPaths.TestSource("Auth", "AuthEndpointTests.cs"));
                foreach (var method in new[]
                         {
                             "AssertAuthCookieAttributes",
                             "Refresh_ValidCookie_Returns204_NewAccessCookieOnly_RefreshNotRotated",
                             "Refresh_WithoutCookie_Returns401_NoSetCookie",
                             "Logout_WithSession_Returns204_RevokesRefresh_ClearsBothCookies",
                         })
                {
                    Assert.Contains(method, authCookies, StringComparison.Ordinal);
                }

                foreach (var file in new[]
                         {
                             Path.Combine("Auth", "TokenServiceTests.cs"),
                             Path.Combine("Auth", "CookieServiceTests.cs"),
                             Path.Combine("Auth", "CookieAuthenticationHandlerTests.cs"),
                         })
                {
                    Assert.True(
                        File.Exists(B17RepoPaths.TestSource(file)),
                        $"Гейт (4) FR-027: не найден тест {file}.");
                }

                break;
            case 5:
                // (5) Матрица ролей — по представителю каждой группы эндпойнтов:
                // labs, groups, submissions.
                var labs = ReadTestSource(B17RepoPaths.TestSource("Labs", "LabsEndpointTests.cs"));
                Assert.Contains("GetList_Student_Returns403ForbiddenEnvelope", labs, StringComparison.Ordinal);
                var groups = ReadTestSource(B17RepoPaths.TestSource("Groups", "GroupsEndpointTests.cs"));
                Assert.Contains("List_Student_Returns403ForbiddenEnvelope", groups, StringComparison.Ordinal);
                var submissions = ReadTestSource(B17RepoPaths.TestSource("Submissions", "SubmissionsEndpointTests.cs"));
                Assert.Contains("Grid_AsStudent_Forbidden", submissions, StringComparison.Ordinal);
                break;
            case 6:
                // (6) labs/groups/students/submissions — тест-проекты эндпойнтов
                // существуют (happy path/404/409/валидация/нормализация page
                // внутри этих файлов).
                foreach (var file in new[]
                         {
                             Path.Combine("Labs", "LabsEndpointTests.cs"),
                             Path.Combine("Groups", "GroupsEndpointTests.cs"),
                             Path.Combine("Students", "StudentsEndpointTests.cs"),
                             Path.Combine("Submissions", "SubmissionsEndpointTests.cs"),
                         })
                {
                    Assert.True(
                        File.Exists(B17RepoPaths.TestSource(file)),
                        $"Гейт (6) FR-027: не найден тест {file}.");
                }

                break;
            case 7:
                // (7) Сид: идемпотентность.
                var seed = ReadTestSource(B17RepoPaths.TestSource("Storage", "SeedRunnerTests.cs"));
                Assert.Contains("Run_Twice_Idempotent", seed, StringComparison.Ordinal);
                break;
            case 8:
                // (8) Конверт ошибок: форма 400/404/500.
                var pipeline = ReadTestSource(B17RepoPaths.TestSource("Hosting", "HostingPipelineTests.cs"));
                Assert.Contains("UnhandledException_Returns500EnvelopeWithoutDetails", pipeline, StringComparison.Ordinal);
                Assert.Contains("FallbackMatrix_MissingIndexHtml_ReturnsNotFoundEnvelope_HealthStillServed", pipeline, StringComparison.Ordinal);
                var modelBinding = ReadTestSource(B17RepoPaths.TestSource("Hosting", "HostingModelBindingTests.cs"));
                Assert.Contains("Bind_MalformedJson_ReturnsErrorEnvelopeWithoutErrors", modelBinding, StringComparison.Ordinal);
                break;
            default:
                Assert.Fail($"Гейт {gate} FR-027 не входит в матрицу (1)–(8).");
                break;
        }
    }

    /// <summary>
    /// Шаблон утверждения «Δkdf(caller)=1» KDF-матрицы входа (харнесс
    /// AuthEndpointHarness тест-проекта бэкенда, снимки IKdfCounter — FR-027).
    /// </summary>
    private static string OneCallerDeltaPattern(string caller) =>
        @"Assert\.Equal\(1,\s*AuthEndpointHarness\.CallerDelta\(before,\s*after,\s*KdfCallers\."
        + caller + @"\)\)";

    /// <summary>Прочитать исходник тест-проекта (с проверкой предусловия).</summary>
    private static string ReadTestSource(string path)
    {
        Assert.True(
            File.Exists(path),
            $"Предусловие кейса: не найден исходник теста {path}.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Тело тест-метода: от первого вхождения имени метода до следующего
    /// атрибута [Fact] (граница следующего теста) или конца файла.
    /// </summary>
    private static string ExtractTestMethodBody(string source, string methodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            $"Инспекция KDF-матрицы (FR-027): не найден тест-метод {methodName}.");
        var nextTest = source.IndexOf("[Fact]", start + methodName.Length, StringComparison.Ordinal);
        return nextTest < 0 ? source[start..] : source[start..nextTest];
    }
}
