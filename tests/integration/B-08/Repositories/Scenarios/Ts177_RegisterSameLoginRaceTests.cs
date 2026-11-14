using LabsApp.IntegrationTests.B08.Repositories.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Repositories.Scenarios;

/// <summary>
/// TS-177 «Атомарность уникальности: параллельные регистрации одного логина»
/// (concurrency, FR-024, FR-006, P1).
///
/// given: логин 'raceuser' свободен; два параллельных POST /auth/register с
///        одинаковым lower(login), разными email.
/// when:  одновременная отправка (оба потока проходят барьеру старта
///        непосредственно перед запросом).
/// then:  один — 201, второй — 409 CONFLICT_LOGIN «Пользователь с таким
///        логином уже существует»; в хранилище один пользователь с
///        lower(login)='raceuser' (виден в GET /students?search=raceuser
///        единственный) (FR-024: lower(login) атомарен).
/// </summary>
public sealed class Ts177_RegisterSameLoginRaceTests : IClassFixture<B08RepositoriesWebAppFactory>
{
    private const string Login = "raceuser";

    private readonly B08RepositoriesWebAppFactory _factory;

    public Ts177_RegisterSameLoginRaceTests(B08RepositoriesWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ParallelRegisterSameLogin_ExactlyOneCreatedOneConflict()
    {
        // given: логин 'raceuser' свободен; email'ы запросов различны.
        using var firstAnonymous = new B08RepositoriesClient(_factory);
        using var secondAnonymous = new B08RepositoriesClient(_factory);
        var firstBody = B08RepositoriesClient.RegisterBody("Гонка Первый", Login, "race-a@example.com");
        var secondBody = B08RepositoriesClient.RegisterBody("Гонка Второй", Login, "race-b@example.com");

        // when: одновременная отправка двух POST /auth/register с барьерой старта.
        using var barrier = new Barrier(participantCount: 2);
        var firstTask = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await firstAnonymous.PostJsonAsync(B08RepositoriesClient.RegisterEndpoint, firstBody);
        });
        var secondTask = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await secondAnonymous.PostJsonAsync(B08RepositoriesClient.RegisterEndpoint, secondBody);
        });
        using var first = await firstTask;
        using var second = await secondTask;

        // then: один — 201, второй — 409 CONFLICT_LOGIN (дословный текст FR-006).
        var statuses = new[] { first.StatusCode, second.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        using var conflictBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            first.StatusCode == HttpStatusCode.Conflict ? first : second,
            HttpStatusCode.Conflict,
            "проигравшая параллельная регистрация (TS-177)");
        Assert.Equal(
            B08RepositoriesClient.LoginDuplicateMessage,
            B08RepositoriesClient.StringProperty(conflictBody.RootElement, "message"));

        // then: в хранилище один пользователь с lower(login)='raceuser'.
        using var teacher = await B08RepositoriesClient.LoginAsTeacherAsync(_factory);
        using var stored = await teacher.GetAsync(
            $"{B08RepositoriesClient.StudentsEndpoint}?search={Uri.EscapeDataString(Login)}");
        using var storedBody = await B08RepositoriesClient.ReadJsonObjectAsync(
            stored, HttpStatusCode.OK, "GET /students?search=raceuser после гонки (TS-177)");
        var items = storedBody.RootElement.GetProperty("items");
        Assert.Equal(1, storedBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(Login, B08RepositoriesClient.StringProperty(items[0], "login"));
    }
}
