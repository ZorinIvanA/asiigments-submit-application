using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-134 «Репозитории: подмена реализаций не требует правок контроллеров»
/// (scope, P0, FR-024 AC «Подмена реализации»).
///
/// given: тестовые реализации интерфейсов IUserRepository, IGroupRepository,
///        ILabRepository, ISubmissionRepository, ISecurityTokenRepository,
///        IRateLimitStore (фиктивные, потокобезопасные), зарегистрированные в
///        WebApplicationFactory вместо in-memory (<see cref="B07RepoSwapWebAppFactory"/>).
/// when:  прогон регистрации + входа + списка работ.
/// then:  все контроллеры работают без изменений их кода: регистрация 201,
///        вход 200, GET /labs 200 на подменных репозиториях (данные ложатся
///        ровно в подмены — Assert.Same разрешений DI и содержимое фейков).
/// </summary>
public sealed class Ts134_RepoSwapControllersUnchangedTests : IClassFixture<B07RepoSwapWebAppFactory>
{
    private const string Login = "swap.user";
    private const string Email = "swap.user@example.com";

    private readonly B07RepoSwapWebAppFactory _factory;

    public Ts134_RepoSwapControllersUnchangedTests(B07RepoSwapWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterLoginAndLabsList_WorkOnSwappedFakeRepositories()
    {
        // given: DI возвращает ровно подмены (факт замены реализаций, а не in-memory).
        var services = _factory.Services;
        Assert.Same(_factory.Users, services.GetRequiredService<IUserRepository>());
        Assert.Same(_factory.Groups, services.GetRequiredService<IGroupRepository>());
        Assert.Same(_factory.Labs, services.GetRequiredService<ILabRepository>());
        Assert.Same(_factory.Submissions, services.GetRequiredService<ISubmissionRepository>());
        Assert.Same(_factory.SecurityTokens, services.GetRequiredService<ISecurityTokenRepository>());
        Assert.Same(_factory.RateLimitStore, services.GetRequiredService<IRateLimitStore>());

        // Сид-преподаватель (FR-025) создан SeedRunner'ом в подмене IUserRepository:
        // SeedRunner резолвит только интерфейсы — правок кода сида не потребовалось.
        var teacher = _factory.Users.GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.NotNull(teacher);

        // when (1): POST /auth/register — регистрация на подменных хранилищах.
        using var client = HostClients.Create(_factory);
        using var registerResponse = await client.PostAsJsonAsync(HostClients.RegisterEndpoint, new
        {
            fullName = "Подменённый Студент",
            login = Login,
            email = Email,
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });

        // then (1): 201; запись создана в ФЕЙКЕ (не в in-memory хранилище приложения);
        // refresh-токен сессии — тоже в подмене ISecurityTokenRepository.
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await BodyAssertions.ReadRootObjectAsync(registerResponse);
        Assert.Equal(Login, registered.GetProperty("login").GetString());
        Assert.Equal("student", registered.GetProperty("role").GetString());

        var stored = _factory.Users.GetByLogin(Login);
        Assert.NotNull(stored);
        Assert.Equal(Email, stored!.Email);
        Assert.Equal("Подменённый Студент", stored.FullName);

        var refreshTokens = _factory.SecurityTokens.RefreshTokensSnapshot();
        var sessionToken = Assert.Single(refreshTokens, token => token.UserId == stored.Id);

        // when (2): POST /auth/login теми же учётными данными.
        using var loginClient = HostClients.Create(_factory);
        using var loginResponse = await loginClient.PostAsJsonAsync(HostClients.LoginEndpoint, new
        {
            login = Login,
            password = HostClients.TestUserPassword,
        });

        // then (2): 200 (хэш пароля проверен Pbkdf2PasswordHasher, сессия выпущена
        // через подмену ISecurityTokenRepository — вторая запись того же пользователя).
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loggedIn = await BodyAssertions.ReadRootObjectAsync(loginResponse);
        Assert.Equal(Login, loggedIn.GetProperty("login").GetString());
        Assert.Equal(2, _factory.SecurityTokens.RefreshTokensSnapshot().Count(token => token.UserId == stored.Id));

        // when (3): GET /labs от teacher (сессия минтится по записи, лежащей в фейке).
        using var labsClient = B07MintedSessions.CreateTeacherClient(_factory);
        using var labsResponse = await labsClient.GetAsync("/api/v1/labs");

        // then (3): 200 — контроллер Labs работает на подменном ILabRepository
        // (хранилище пусто: Seed__DemoData=false, создания работ кейсом не было).
        Assert.Equal(HttpStatusCode.OK, labsResponse.StatusCode);
        var page = await BodyAssertions.ReadRootObjectAsync(labsResponse);
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Empty(_factory.Labs.GetAll());

        // then: запись refresh-сессии жива в подмене (ленивая живость по TimeProvider).
        Assert.NotNull(_factory.SecurityTokens.FindLiveByHash(sessionToken.TokenHash));

        // then: состояние регистрационного лимитера осело в подмене IRateLimitStore
        // (политика register: попытка регистрации учтена в подменном хранилище).
        Assert.True(
            _factory.RateLimitStore.TrackedKeysCount(RateLimitPolicies.Register) > 0,
            "Ожидалась отметка попытки регистрации в подмене IRateLimitStore (политика register).");
        Assert.Single(_factory.RateLimitStore.GetKeys(RateLimitPolicies.Register));
    }
}
