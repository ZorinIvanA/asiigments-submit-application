using LabsApp.IntegrationTests.B08.Repositories.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Repositories.Scenarios;

/// <summary>
/// TS-175 «Подмена реализаций репозиториев не требует правок контроллеров»
/// (happy_path, FR-024, P0) — HTTP-половина кейса.
///
/// given: тестовые реализации интерфейсов персистенции FR-024
///        (IUserRepository, IGroupRepository, ILabRepository,
///        ISubmissionRepository, ISecurityTokenRepository, IRateLimitStore)
///        зарегистрированы в DI ВМЕСТО in-memory (записывающий декоратор над
///        живым штатным дескриптором — состав членов интерфейсов в волне
///        реворка меняется, поэтому подмена не компилируется против
///        конкретных сигнатур; контроллеры и сервисы при этом не правятся).
/// when:  прогон репрезентативного сквозного потока: register → login →
///        create group/lab → назначение студента → upsert сдачи.
/// then:  все операции завершаются корректно (201/200/204) без изменения кода
///        контроллеров; все шесть интерфейсов FR-024 подменены; вызовы
///        хранилища прошли через тестовые реализации (FR-024 AC «Подмена
///        реализации»). Статическая половина кейса —
///        <see cref="Ts175_StaticNoInMemoryUsingsTests"/>.
/// </summary>
public sealed class Ts175_SubstitutionFlowTests : IClassFixture<B08RepositoriesSubstitutionFactory>
{
    private const string StudentLogin = "subststudent";
    private const string StudentEmail = "subststudent@example.com";
    private const string StudentFullName = "Подменённый Студент";

    private readonly B08RepositoriesSubstitutionFactory _factory;

    public Ts175_SubstitutionFlowTests(B08RepositoriesSubstitutionFactory factory) => _factory = factory;

    [Fact]
    public async Task EndToEndFlow_WorksOverSubstitutedRepositories()
    {
        // when: register (шаг 1 сквозного потока).
        using var anonymous = new B08RepositoriesClient(_factory);
        using var registered = await anonymous.PostJsonAsync(
            B08RepositoriesClient.RegisterEndpoint,
            B08RepositoriesClient.RegisterBody(StudentFullName, StudentLogin, StudentEmail));
        using var registeredBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            registered, HttpStatusCode.Created, "POST /auth/register над подмененными репозиториями (TS-175)");
        Assert.Equal(
            "student",
            B08RepositoriesClient.StringProperty(registeredBody.RootElement, "role"));

        // when: login зарегистрированного студента (шаг 2).
        await anonymous.LoginAsync(StudentLogin, "Passw0rd!");

        // when: create group и create lab — сессия teacher (шаг 3).
        using var teacher = await B08RepositoriesClient.LoginAsTeacherAsync(_factory);
        using var groupResponse = await teacher.PostJsonAsync(
            B08RepositoriesClient.GroupsEndpoint,
            "{\"name\":" + B08RepositoriesClient.JsonString("Подменённая группа") + "}");
        using var groupBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            groupResponse, HttpStatusCode.Created, "POST /groups над подмененными репозиториями (TS-175)");
        var groupId = B08RepositoriesClient.StringProperty(groupBody.RootElement, "id");

        using var labResponse = await teacher.PostJsonAsync(
            B08RepositoriesClient.LabsEndpoint,
            "{\"number\":1,\"semester\":5,\"content\":\"Подменённая работа\"," +
            "\"assignmentUrl\":null,\"defenseRequired\":false}");
        using var labBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            labResponse, HttpStatusCode.Created, "POST /labs над подмененными репозиториями (TS-175)");
        var labId = B08RepositoriesClient.StringProperty(labBody.RootElement, "id");

        // when: назначение студента в группу (шаг 4; uuid студента — из выдачи
        // GET /students?search, контракт IF-011).
        var studentId = await FindStudentIdAsync(teacher, StudentLogin);
        using var assigned = await teacher.PutJsonAsync(
            $"{B08RepositoriesClient.StudentsEndpoint}/{studentId}/group",
            "{\"groupId\":" + B08RepositoriesClient.JsonString(groupId) + "}");
        Assert.True(
            assigned.StatusCode == HttpStatusCode.NoContent,
            $"PUT /students/{{id}}/group над подмененными репозиториями → ожидался 204, фактически " +
            $"{assigned.StatusCode}: {await B08RepositoriesClient.ReadBodyAsync(assigned)}");

        // when: upsert сдачи (шаг 5).
        using var submission = await teacher.PutJsonAsync(
            B08RepositoriesClient.SubmissionsEndpoint,
            "{\"studentId\":" + B08RepositoriesClient.JsonString(studentId) +
            ",\"labId\":" + B08RepositoriesClient.JsonString(labId) +
            ",\"submitDate\":\"2026-10-10\",\"defenseDate\":null}");
        using var submissionBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            submission, HttpStatusCode.OK, "PUT /submissions над подмененными репозиториями (TS-175)");
        Assert.Equal(
            studentId,
            B08RepositoriesClient.StringProperty(submissionBody.RootElement, "studentId"));

        // then: все шесть интерфейсов FR-024 подменены тестовыми реализациями.
        var substituted = new HashSet<string>(_factory.Recorder.SubstitutedInterfaces);
        foreach (var interfaceName in B08RepositoriesSubstitutionFactory.PersistedInterfaceNames)
        {
            Assert.True(
                substituted.Contains(interfaceName),
                $"Интерфейс {interfaceName} (FR-024) не найден/не подменен в DI тестового хоста.");
        }

        // then: поток хранилища прошёл через подмененные реализации —
        // контроллеры работают без изменений их кода (FR-024 AC).
        Assert.True(
            _factory.Recorder.CountInvocationsOf("IUserRepository") >= 1,
            "Вызовы IUserRepository не зафиксированы на тестовой реализации (контроллеры используют штатную регистрацию?).");
        Assert.True(
            _factory.Recorder.CountInvocationsOf("IGroupRepository") >= 1,
            "Вызовы IGroupRepository не зафиксированы на тестовой реализации (контроллеры используют штатную регистрацию?).");
        Assert.True(
            _factory.Recorder.CountInvocationsOf("ILabRepository") >= 1,
            "Вызовы ILabRepository не зафиксированы на тестовой реализации (контроллеры используют штатную регистрацию?).");
        Assert.True(
            _factory.Recorder.CountInvocationsOf("ISubmissionRepository") >= 1,
            "Вызовы ISubmissionRepository не зафиксированы на тестовой реализации (контроллеры используют штатную регистрацию?).");
    }

    private static async Task<string> FindStudentIdAsync(B08RepositoriesClient teacher, string login)
    {
        using var page = await teacher.GetAsync($"{B08RepositoriesClient.StudentsEndpoint}?search={Uri.EscapeDataString(login)}");
        using var body = await B08RepositoriesClient.ReadJsonObjectAsync(
            page, HttpStatusCode.OK, $"GET /students?search={login} (TS-175)");
        var items = body.RootElement.GetProperty("items");
        Assert.True(
            items.GetArrayLength() == 1,
            $"Ожидался ровно один студент по поиску «{login}», фактически {items.GetArrayLength()}.");
        return B08RepositoriesClient.StringProperty(items[0], "id");
    }
}

/// <summary>
/// TS-175 (FR-024, P0) — статическая половина кейса.
///
/// given: исходники продуктового проекта src/api/LabsApp (*.cs, рекурсивно,
///        без bin/obj); in-memory реализации живут в LabsApp.Storage.InMemory
///        и LabsApp.Auth.RateLimiting (InMemoryRateLimitStore).
/// when:  статический поиск директив «using LabsApp.Storage.InMemory» по всем
///        *.cs проекта, КРОМЕ composition-root'ов DI (файлы регистраций
///        StorageServiceCollectionExtensions.cs и AuthCoreServiceCollection-
///        Extensions.cs — единственное легитимное место связи интерфейсов
///        с in-memory реализациями) и самих файлов реализаций; наряду с ним —
///        поиск подстроки «InMemory» по каталогу Controllers/ (контроллеру
///        нечего делать с именами реализаций даже в комментарии).
/// then:  ни один файл контроллеров и сервисов не ссылается на InMemory-
///        реализации (только на интерфейсы — FR-024: «зависеть ТОЛЬКО от
///        интерфейсов (конструкторная инъекция)»).
/// </summary>
public sealed class Ts175_StaticNoInMemoryUsingsTests
{
    /// <summary>Запрещённая using-директива (namespace in-memory реализаций хранилищ).</summary>
    private const string ForbiddenUsing = "using LabsApp.Storage.InMemory";

    /// <summary>
    /// Файлы реализаций и composition-root'ов DI, где упоминание in-memory
    /// реализаций легитимно (относительные пути от src/api/LabsApp).
    /// </summary>
    private static readonly string[] AllowedFiles =
    [
        $"Storage{Path.DirectorySeparatorChar}InMemory",
        $"Auth{Path.DirectorySeparatorChar}RateLimiting{Path.DirectorySeparatorChar}InMemoryRateLimitStore.cs",
        "Program.cs",
        $"Storage{Path.DirectorySeparatorChar}StorageServiceCollectionExtensions.cs",
        $"Auth{Path.DirectorySeparatorChar}AuthCoreServiceCollectionExtensions.cs",
    ];

    [Fact]
    public async Task ControllersAndServices_HaveNoUsingOfInMemoryImplementations()
    {
        // given: исходники продуктового проекта src/api/LabsApp.
        var productProjectDir = FindProductProjectDirectory();

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(productProjectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsSourceFile(file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(productProjectDir, file);
            if (IsAllowedFile(relative))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(file);
            if (content.Contains(ForbiddenUsing, StringComparison.Ordinal))
            {
                violations.Add($"{relative}: {ForbiddenUsing}");
            }

            if (relative.Split(Path.DirectorySeparatorChar).Contains("Controllers")
                && content.Contains("InMemory", StringComparison.Ordinal))
            {
                violations.Add($"{relative}: упоминание in-memory реализации в контроллере");
            }
        }

        Assert.True(
            violations.Count == 0,
            "FR-024 («контроллеры и прикладные сервисы зависят ТОЛЬКО от интерфейсов») нарушен: " +
            "найдены ссылки на InMemory-реализации вне composition-root DI:\n" +
            string.Join("\n", violations));
    }

    /// <summary>Ни один сегмент пути — не bin/ и не obj/ (сборочные артефакты).</summary>
    private static bool IsSourceFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(segment => segment is "bin" or "obj");
    }

    /// <summary>Файл в зоне легитимных упоминаний (реализации, composition-root DI).</summary>
    private static bool IsAllowedFile(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return AllowedFiles.Any(allowed => normalized.StartsWith(allowed, StringComparison.Ordinal));
    }

    /// <summary>
    /// Каталог src/api/LabsApp репозитория — вверх по дереву от каталога
    /// сборки тестовой зоны (детерминированно: зона живёт внутри репозитория).
    /// </summary>
    private static string FindProductProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "api", "LabsApp");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new Xunit.Sdk.XunitException(
            $"Каталог src/api/LabsApp не найден вверх по дереву от {AppContext.BaseDirectory} (TS-175).");
    }
}
