using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-043 «Дубликат email при свободном логине» (negative, P0,
/// FR-012 AC «Дубликат email при свободном логине»).
///
/// given: email a@b.ru занят другим пользователем (DI-сид, ADR-010); логин свободен;
///        RemoteIpAddress=10.0.0.43 (лимит регистраций на IP не исчерпан).
/// when:  регистрация с этим email.
/// then:  409 «Пользователь с таким email уже существует».
/// </summary>
public sealed class Ts043_DuplicateEmailTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts043_DuplicateEmailTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithOccupiedEmailAndFreeLogin_Returns409DuplicateEmail()
    {
        // given: email a@b.ru занят другим пользователем.
        TestSessions.SeedUser(
            _factory,
            login: "ts043.email.owner",
            email: "a@b.ru",
            fullName: "Владелец Email");

        using var client = HostClients.Create(_factory);

        // when: регистрация с этим email и свободным логином.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.43",
            fullName: "Дубль Email",
            login: "ts043.user",
            email: "a@b.ru",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 409 с текстом словаря (errors у 409 нет — только message, IF-001).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Пользователь с таким email уже существует");
    }
}
