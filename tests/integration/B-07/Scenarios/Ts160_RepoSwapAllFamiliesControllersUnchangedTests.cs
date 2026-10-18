using System.Text;
using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-160 «Репозитории: подмена реализаций не требует правок контроллеров»
/// (happy_path, P1, FR-024 AC «Подмена реализации»).
///
/// given: тестовый хост с зарегистрированными тестовыми двойниками
///        IUserRepository, IGroupRepository, ILabRepository,
///        ISubmissionRepository, ISecurityTokenRepository, IRateLimitStore
///        (потокобезопасные заглушки <see cref="B07FakeRepositories"/>); код
///        контроллеров не изменялся (зона ссылается на production-сборку
///        LabsApp через ProjectReference, заменяются ТОЛЬКО регистрации DI).
/// when:  вызовы представителей каждого семейства: POST /auth/register,
///        POST /auth/login, POST /auth/refresh, GET /labs и POST /labs
///        (teacher), POST /groups, PUT /students/{id}/group, PUT /submissions.
/// then:  все эндпойнты работают без изменений кода контроллеров: статусы по
///        бизнес-правилам (200/201/204/400/401), ни одного 500.
/// </summary>
public sealed class Ts160_RepoSwapAllFamiliesControllersUnchangedTests : IClassFixture<B07RepoSwapWebAppFactory>
{
    private const string Login = "t160-student";
    private const string Email = "t160-student@example.com";
    private const string GroupName = "Т-160 группа подмены";
    private const int LabSemester = 3;
    private const int LabNumber = 5;

    private readonly B07RepoSwapWebAppFactory _factory;

    /// <summary>Все статусы сценария — для итоговой сверки «ни одного 500».</summary>
    private readonly List<HttpStatusCode> _observedStatuses = [];

    public Ts160_RepoSwapAllFamiliesControllersUnchangedTests(B07RepoSwapWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AllEndpointFamilies_WorkOnSwappedStubs_WithoutAny500()
    {
        // given: DI отдаёт ровно тестовых двойников (факт подмены всех ШЕСТИ
        // интерфейсов FR-024); контроллеры — немодифицированный прод-код
        // (ProjectReference на LabsApp, зона реализации не менялась).
        var services = _factory.Services;
        Assert.Same(_factory.Users, services.GetRequiredService<IUserRepository>());
        Assert.Same(_factory.Groups, services.GetRequiredService<IGroupRepository>());
        Assert.Same(_factory.Labs, services.GetRequiredService<ILabRepository>());
        Assert.Same(_factory.Submissions, services.GetRequiredService<ISubmissionRepository>());
        Assert.Same(_factory.SecurityTokens, services.GetRequiredService<ISecurityTokenRepository>());
        Assert.Same(_factory.RateLimitStore, services.GetRequiredService<IRateLimitStore>());

        // Сид-преподаватель осел в подмене IUserRepository (SeedRunner резолвит
        // только интерфейсы — правок кода сида не потребовалось).
        Assert.NotNull(_factory.Users.GetByLogin(SeedOptions.DefaultTeacherLogin));

        // when/then (1): POST /auth/register — 201, запись в подмене IUserRepository.
        using var registerClient = HostClients.Create(_factory);
        using var registerResponse = await registerClient.PostAsJsonAsync(HostClients.RegisterEndpoint, new
        {
            fullName = "Студент Подмены Т-160",
            login = Login,
            email = Email,
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });
        Assert.Equal(HttpStatusCode.Created, Track(registerResponse));
        var studentId = _factory.Users.GetByLogin(Login)?.Id
            ?? throw new InvalidOperationException("Студент не осел в подмене IUserRepository.");

        // when/then (2): POST /auth/login — 200 по бизнес-правилу; неверный
        // пароль — 401 (второе бизнес-правило того же семейства).
        using var loginClient = HostClients.Create(_factory);
        using var loginResponse = await loginClient.PostAsJsonAsync(HostClients.LoginEndpoint, new
        {
            login = Login,
            password = HostClients.TestUserPassword,
        });
        Assert.Equal(HttpStatusCode.OK, Track(loginResponse));

        using var wrongPasswordClient = HostClients.Create(_factory);
        using var wrongPasswordResponse = await wrongPasswordClient.PostAsJsonAsync(HostClients.LoginEndpoint, new
        {
            login = Login,
            password = "Wrong0rd!pass",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, Track(wrongPasswordResponse));

        // when/then (3): POST /auth/refresh — 204 по живому refresh-cookie
        // (запись в подмене ISecurityTokenRepository); без cookie — 401.
        using var refreshResponse = await loginClient.PostAsync("/api/v1/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.NoContent, Track(refreshResponse));

        using var anonymousRefreshClient = HostClients.Create(_factory);
        using var anonymousRefreshResponse = await anonymousRefreshClient.PostAsync("/api/v1/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, Track(anonymousRefreshResponse));

        // when/then (4): GET /labs (teacher) — 200; POST /labs — 201; битое
        // тело POST /labs — 400 (ветка валидации того же семейства).
        using var teacherClient = B07MintedSessions.CreateTeacherClient(_factory);
        using var labsListResponse = await teacherClient.GetAsync("/api/v1/labs");
        Assert.Equal(HttpStatusCode.OK, Track(labsListResponse));

        using var labCreateResponse = await teacherClient.PostAsJsonAsync("/api/v1/labs", new
        {
            number = LabNumber,
            semester = LabSemester,
            content = "Работа Т-160 на подменных репозиториях",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });
        Assert.Equal(HttpStatusCode.Created, Track(labCreateResponse));
        var labCreated = await BodyAssertions.ReadRootObjectAsync(labCreateResponse);
        var labId = labCreated.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(labId), "Ожидался непустой id работы в теле 201.");

        using var malformedLabResponse = await teacherClient.PostAsync(
            "/api/v1/labs",
            new StringContent("{ не json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, Track(malformedLabResponse));

        // when/then (5): POST /groups (teacher) — 201.
        using var groupCreateResponse = await teacherClient.PostAsJsonAsync("/api/v1/groups", new { name = GroupName });
        Assert.Equal(HttpStatusCode.Created, Track(groupCreateResponse));
        var groupCreated = await BodyAssertions.ReadRootObjectAsync(groupCreateResponse);
        var groupId = groupCreated.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(groupId), "Ожидался непустой id группы в теле 201.");

        // when/then (6): PUT /students/{id}/group (teacher) — 204 (Success).
        using var setGroupResponse = await teacherClient.PutAsJsonAsync(
            $"/api/v1/students/{studentId}/group",
            new { groupId });
        Assert.Equal(HttpStatusCode.NoContent, Track(setGroupResponse));

        // when/then (7): PUT /submissions (teacher) — 200 (upsert дат сдачи).
        using var submissionResponse = await teacherClient.PutAsJsonAsync("/api/v1/submissions", new
        {
            studentId = studentId.ToString(),
            labId,
            submitDate = "2026-10-01",
            defenseDate = (string?)"2026-10-10",
        });
        Assert.Equal(HttpStatusCode.OK, Track(submissionResponse));

        // then: ни одного 500 на всей последовательности вызовов.
        Assert.True(
            _observedStatuses.All(status => (int)status < 500),
            "Обнаружены ответы класса 5xx: " + string.Join(", ", _observedStatuses));

        // then: данные осели ровно в подменах (контроллеры работали на тестовых
        // двойниках, а не на in-memory реализациях приложения).
        var storedStudent = _factory.Users.GetById(studentId);
        Assert.NotNull(storedStudent);
        Assert.Equal(Guid.Parse(groupId!), storedStudent!.GroupId);

        var storedSubmissions = _factory.Submissions.ListByStudent(studentId);
        var submission = Assert.Single(storedSubmissions);
        Assert.Equal(Guid.Parse(labId!), submission.LabId);
        Assert.Equal(new DateOnly(2026, 10, 1), submission.SubmitDate);
        Assert.Single(_factory.Labs.GetAll(), lab => lab.Semester == LabSemester && lab.Number == LabNumber);
        Assert.Single(_factory.Groups.GetAll(), group => group.Name == GroupName);
    }

    /// <summary>Фиксирует статус ответа в список наблюдения и возвращает его.</summary>
    private HttpStatusCode Track(HttpResponseMessage response)
    {
        _observedStatuses.Add(response.StatusCode);
        return response.StatusCode;
    }
}
