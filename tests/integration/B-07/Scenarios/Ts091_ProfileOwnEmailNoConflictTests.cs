using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-091 «PUT /me/profile со своим email: 200, без конфликта» (idempotency, FR-021, P1).
///
/// given: сессия с email a@b.ru (собственная учётка регистрации).
/// when:  PUT /me/profile {fullName:'Имя', email:'a@b.ru'} — свой текущий email.
/// then:  200. FR-021 AC «Свой email»: «Свой неизменённый email конфликтом не
///        считается» (ci-уникальность не считает собственный email занятым).
/// </summary>
public sealed class Ts091_ProfileOwnEmailNoConflictTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts091";
    private const string Email = "a@b.ru";

    private readonly B07WebAppFactory _factory;

    public Ts091_ProfileOwnEmailNoConflictTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithOwnCurrentEmail_Returns200()
    {
        // given: сессия с email a@b.ru.
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: "Имя", login: Login, email: Email);

        // when: сохранение со своим текущим email.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = Email,
        });

        // then: 200 — свой email конфликтом не считается.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
