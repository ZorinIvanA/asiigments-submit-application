using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-147 «Конкурентная регистрация одного логина: один 201, один 409»
/// (concurrency, FR-007, FR-002).
///
/// given: свежий экземпляр (собственная фикстура класса — счётчик регистраций
///        пуст, 2 попытки — лимит 5/час не задет); логин 'raceuser' свободен;
///        два параллельных запроса регистрации.
/// when:  параллельно два POST /api/v1/auth/register {login:'raceuser', разные
///        email, валидные поля}; затем GET /api/v1/students?search=raceuser
///        (входом преподавателя).
/// then:  один ответ — 201, другой — 409 «Пользователь с таким логином уже
///        существует»; в списке студентов ровно один raceuser. FR-007:
///        уникальность login ci; FR-002: потокобезопасность.
/// </summary>
public sealed class Ts147_ConcurrentRegisterRaceTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts147_ConcurrentRegisterRaceTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TwoConcurrentRegistersOfSameLogin_ExactlyOneCreatedAndOneConflict()
    {
        // given: два параллельных запроса регистрации с одним login и разными email.
        using var clientA = HostClients.Create(_factory);
        using var clientB = HostClients.Create(_factory);

        var firstTask = clientA.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Гонка Регистраций Первый",
            login = "raceuser",
            email = "race-one@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });
        var secondTask = clientB.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Гонка Регистраций Второй",
            login = "raceuser",
            email = "race-two@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });
        var responses = await Task.WhenAll(firstTask, secondTask);
        using (responses[0])
        using (responses[1])
        {
            // then: один ответ — 201, другой — 409 с дословным текстом словаря.
            var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToList();
            Assert.Equal(
                new List<HttpStatusCode> { HttpStatusCode.Created, HttpStatusCode.Conflict },
                statuses);
            var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            var conflictBody = await BodyAssertions.ReadRootObjectAsync(conflict);
            BodyAssertions.MessageIs(conflictBody, "Пользователь с таким логином уже существует");
        }

        // затем: GET /api/v1/students?search=raceuser (входом преподавателя) —
        // в списке ровно один raceuser.
        using var teacherClient = await HostClients.CreateTeacherClientAsync(_factory);
        using var students = await teacherClient.GetAsync("/api/v1/students?search=raceuser");
        Assert.Equal(HttpStatusCode.OK, students.StatusCode);

        var root = await BodyAssertions.ReadRootObjectAsync(students);
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        var raceUsers = items.EnumerateArray()
            .Where(item => item.GetProperty("login").GetString() == "raceuser")
            .ToList();
        Assert.Single(raceUsers);
    }
}
