using LabsApp.IntegrationTests.B08.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Groups.Scenarios;

/// <summary>
/// TS-165 «Репозитории: подмена реализаций не требует правок контроллеров»
/// (happy_path, FR-024, P1).
///
/// given: тестовые реализации интерфейсов персистенции FR-024
///        (IUserRepository, IGroupRepository, ILabRepository,
///        ISubmissionRepository, ISecurityTokenRepository, IRateLimitStore)
///        зарегистрированы в DI ВМЕСТО штатных (записывающий декоратор над
///        живым штатным дескриптором — состав членов интерфейсов в волне
///        реворка меняется, поэтому подмена не компилируется против
///        конкретных сигнатур; контроллеры и сервисы при этом не правятся).
/// when:  прогон базовых сценариев: register → login → GET /labs.
/// then:  все статусы контрактные (201 / 200 / 200); все шесть интерфейсов
///        FR-024 подменены; вызовы хранилища прошли через тестовые
///        реализации (IUserRepository, ILabRepository) — контроллеры работают
///        без изменений их кода (FR-024 AC «Подмена реализации»).
/// </summary>
public sealed class Ts165_RepositorySubstitutionTests : IClassFixture<B08GroupsSubstitutionFactory>
{
    private const string Login = "substuser";
    private const string Email = "substuser@example.com";
    private const string FullName = "Подменённый Репозиторий";

    private readonly B08GroupsSubstitutionFactory _factory;

    public Ts165_RepositorySubstitutionTests(B08GroupsSubstitutionFactory factory) => _factory = factory;

    [Fact]
    public async Task BasicFlow_WorksOverSubstitutedPersistenceInterfaces()
    {
        using var client = new B08GroupsClient(_factory);

        // when: POST /auth/register (базовый сценарий, шаг 1).
        using var registered = await client.PostJsonAsync(
            B08GroupsClient.RegisterEndpoint,
            "{\"fullName\":" + B08GroupsClient.JsonString(FullName) +
            ",\"login\":" + B08GroupsClient.JsonString(Login) +
            ",\"email\":" + B08GroupsClient.JsonString(Email) +
            ",\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}");

        // when: POST /auth/login сид-преподавателя (шаг 2); GET /labs (шаг 3).
        await client.LoginAsync(B08GroupsWebAppFactory.TeacherLogin, B08GroupsWebAppFactory.TestTeacherPassword);
        using var labs = await client.GetAsync(B08GroupsClient.LabsEndpoint);

        // then: контроллеры отвечают контрактно поверх тестовых реализаций.
        Assert.True(
            registered.StatusCode == HttpStatusCode.Created,
            $"POST /auth/register над подмененными репозиториями → ожидался 201, фактически " +
            $"{registered.StatusCode}: {await B08GroupsClient.ReadBodyAsync(registered)}");
        Assert.True(
            labs.StatusCode == HttpStatusCode.OK,
            $"GET /labs над подмененными репозиториями → ожидался 200, фактически " +
            $"{labs.StatusCode}: {await B08GroupsClient.ReadBodyAsync(labs)}");

        // then: все шесть интерфейсов FR-024 подменены тестовыми реализациями.
        var substituted = new HashSet<string>(_factory.Recorder.SubstitutedInterfaces);
        foreach (var interfaceName in B08GroupsSubstitutionFactory.PersistedInterfaceNames)
        {
            Assert.True(
                substituted.Contains(interfaceName),
                $"Интерфейс {interfaceName} (FR-024) не найден/не подменен в DI тестового хоста.");
        }

        // then: поток хранилища прошёл через подмененные реализации
        // (регистрация читает/пишет пользователей, список работ — репозиторий работ).
        Assert.True(
            _factory.Recorder.CountInvocationsOf("IUserRepository") >= 1,
            "Вызовы IUserRepository не зафиксированы на тестовой реализации (контроллеры используют штатную регистрацию?).");
        Assert.True(
            _factory.Recorder.CountInvocationsOf("ILabRepository") >= 1,
            "Вызовы ILabRepository не зафиксированы на тестовой реализации (GET /labs минует интерфейс FR-024?).");
    }
}
