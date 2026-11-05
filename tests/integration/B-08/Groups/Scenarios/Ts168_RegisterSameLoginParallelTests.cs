using LabsApp.IntegrationTests.B08.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Groups.Scenarios;

/// <summary>
/// TS-168 «Атомарность уникальности: параллельные регистрации одного логина»
/// (concurrency, FR-024, FR-006, P1).
///
/// given: логин 'raceuser' свободен; email-адреса запросов различны и
///        свободны.
/// when:  два параллельных POST /auth/register с login 'raceuser'.
/// then:  ровно один 201, другой — 409 «Пользователь с таким логином уже
///        существует»; в хранилище один пользователь с lower(login)='raceuser'
///        (виден в GET /students?search=raceuser единственный).
/// </summary>
public sealed class Ts168_RegisterSameLoginParallelTests : IClassFixture<B08GroupsWebAppFactory>
{
    private const string Login = "raceuser";

    private readonly B08GroupsWebAppFactory _factory;

    public Ts168_RegisterSameLoginParallelTests(B08GroupsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelRegisterSameLogin_ExactlyOneCreatedOneConflict()
    {
        // given: логин 'raceuser' свободен; email'ы различны и свободны.
        using var anonymous = new B08GroupsClient(_factory);
        var firstBody = RegisterBody("race-a@example.com");
        var secondBody = RegisterBody("race-b@example.com");

        // when: два параллельных POST /auth/register с одним логином.
        var firstTask = anonymous.PostJsonAsync(B08GroupsClient.RegisterEndpoint, firstBody);
        var secondTask = anonymous.PostJsonAsync(B08GroupsClient.RegisterEndpoint, secondBody);
        using var first = await firstTask;
        using var second = await secondTask;

        // then: ровно один 201, другой — 409 «Пользователь с таким логином уже существует».
        var statuses = new[] { first.StatusCode, second.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        using var conflictBody = await B08GroupsClient.ReadJsonObjectAsync(
            first.StatusCode == HttpStatusCode.Conflict ? first : second,
            HttpStatusCode.Conflict,
            "проигравшая параллельная регистрация (TS-168)");
        Assert.Equal(
            B08GroupsClient.LoginDuplicateMessage,
            B08GroupsClient.StringProperty(conflictBody.RootElement, "message"));

        // then: в хранилище один пользователь с login 'raceuser'.
        using var teacher = await B08GroupsClient.LoginAsTeacherAsync(_factory);
        using var stored = await teacher.GetAsync($"{B08GroupsClient.StudentsEndpoint}?search={Login}");
        using var storedBody = await B08GroupsClient.ReadJsonObjectAsync(
            stored, HttpStatusCode.OK, "GET /students?search=raceuser после гонки (TS-168)");
        Assert.Equal(1, storedBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            new[] { Login },
            B08GroupsClient.ReadLogins(storedBody.RootElement));
    }

    private static string RegisterBody(string email) =>
        "{\"fullName\":\"Гонка ОдноЛогин\",\"login\":\"" + Login + "\"," +
        "\"email\":\"" + email + "\",\"password\":\"Passw0rd!\",\"repeatPassword\":\"Passw0rd!\"}";
}
