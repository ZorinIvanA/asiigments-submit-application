using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-044 «Порядок 409: дубликат login проверяется раньше дубликата email»
/// (negative, P1, FR-012 description + ASM-009: «в register дубликат login
/// проверяется раньше email»).
///
/// given: пользователь A с login='teacher' (сид преподавателя FR-006); пользователь B
///        с email='b@x.ru' (DI-сид); RemoteIpAddress=10.0.0.44 (лимит не исчерпан).
/// when:  регистрация {login:'teacher', email:'b@x.ru', прочее валидно}.
/// then:  409 с текстом «Пользователь с таким логином уже существует» (не про email).
/// </summary>
public sealed class Ts044_DuplicateLoginBeforeEmailTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts044_DuplicateLoginBeforeEmailTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithBothDuplicates_ReturnsLoginDuplicateError()
    {
        // given: пользователь B с email='b@x.ru'; пользователь A (login='teacher') —
        // сид преподавателя фикстуры.
        TestSessions.SeedUser(
            _factory,
            login: "ts044.b.owner",
            email: "b@x.ru",
            fullName: "Владелец B");

        using var client = HostClients.Create(_factory);

        // when: регистрация с ОБОИМИ дубликатами.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.44",
            fullName: "Порядок Конфликтов",
            login: "teacher",
            email: "b@x.ru",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 409 именно про логин — дубликат login проверяется раньше email.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Пользователь с таким логином уже существует");
    }
}
