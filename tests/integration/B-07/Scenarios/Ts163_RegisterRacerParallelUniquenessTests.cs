using LabsApp.Domain;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-163 «Репозитории: параллельная регистрация одного логина — один 201»
/// (concurrency, P0, FR-024 + FR-006 — атомарность уникальности lower(login)).
///
/// given: логин 'racer' свободен; два параллельных клиента (барьер старта);
///        тела с разными email.
/// when:  два одновременных POST /auth/register с login 'racer'.
/// then:  ровно один 201, другой — 409 «Пользователь с таким логином уже
///        существует»; в хранилище один пользователь с этим lower(login)
///        (атомарность уникальности lower(login)).
/// </summary>
public sealed class Ts163_RegisterRacerParallelUniquenessTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "racer";
    private const string FirstEmail = "racer-first@example.com";
    private const string SecondEmail = "racer-second@example.com";

    private readonly B07WebAppFactory _factory;

    public Ts163_RegisterRacerParallelUniquenessTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwoConcurrentRegistrationsOfRacer_ExactlyOne201OtherConflict_SingleStoredUser()
    {
        // given: логин 'racer' свободен.
        Assert.Null(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(Login));

        using var firstClient = HostClients.Create(_factory);
        using var secondClient = HostClients.Create(_factory);

        // when: два одновременных POST /auth/register с login 'racer'
        // (барьер старта; тела различаются только email).
        using var startGate = new ManualResetEventSlim();
        var firstTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await RegisterAsync(firstClient, FirstEmail, "Гонщик Первый");
        });
        var secondTask = Task.Run(async () =>
        {
            startGate.Wait();
            return await RegisterAsync(secondClient, SecondEmail, "Гонщик Второй");
        });

        startGate.Set();
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        // then: ровно один 201, другой — 409 с дословным текстом
        // «Пользователь с таким логином уже существует»; ни одного 5xx.
        var responses = new[] { firstResponse, secondResponse };
        Assert.All(responses, response => Assert.True((int)response.StatusCode < 500, $"Неожиданный 5xx: {(int)response.StatusCode}"));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        using var conflictResponse = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflict = await BodyAssertions.ReadRootObjectAsync(conflictResponse);
        BodyAssertions.MessageIs(conflict, ErrorTexts.DuplicateLogin);

        // then: в хранилище один пользователь с lower(login)='racer'; это
        // победитель гонки — его email сохранён, email проигравшего отсутствует.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var winners = new[] { firstResponse, secondResponse }
            .Zip(new[] { FirstEmail, SecondEmail }, (response, email) => (response, email))
            .Where(pair => pair.response.StatusCode == HttpStatusCode.Created)
            .Select(pair => pair.email)
            .ToArray();
        var winnerEmail = Assert.Single(winners);
        var loserEmail = winnerEmail == FirstEmail ? SecondEmail : FirstEmail;

        var storedStudents = users.ListStudents();
        var racer = Assert.Single(storedStudents, user => Collation.Key(user.Login) == Login);
        Assert.Equal(winnerEmail, racer.Email);
        Assert.Equal(Login, Collation.Key(racer.Login));

        var byLoginCi = users.GetByLogin("RACER");
        Assert.NotNull(byLoginCi);
        Assert.Equal(racer.Id, byLoginCi!.Id);
        Assert.Null(users.GetByEmail(loserEmail));
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string fullName) =>
        client.PostAsJsonAsync(HostClients.RegisterEndpoint, new
        {
            fullName,
            login = Login,
            email,
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });
}
