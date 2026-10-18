using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-090 «PUT /me/profile: чужой занятый email ci → 409» (negative, FR-021, P0).
///
/// given: пользователь A role=student (свой email, отличный от b@x.ru) и
///        пользователь B role=student с email 'b@x.ru' созданы прямым DI-сидом
///        в IUserRepository тестового хоста (NFR-001: публичный API для сида
///        не используется); сессия A — cookie access_token с JWT HS256, минтым
///        харнесом ключом Auth__JwtKey тестового хоста; POST /auth/register и
///        POST /auth/login в предусловиях НЕ вызываются (CR-001, арбитраж a-017).
/// when:  PUT /me/profile {fullName:текущее, email:'b@x.ru'}.
/// then:  409 «Пользователь с таким email уже существует».
///        FR-021 AC «Чужой занятый email».
/// </summary>
public sealed class Ts090_ProfileForeignEmailConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string LoginA = "ts090a";
    private const string EmailA = "a090@x.ru";
    private const string FullNameA = "Пользователь А";
    private const string LoginB = "ts090b";
    private const string EmailB = "b@x.ru";
    private const string FullNameB = "Пользователь Б";

    private readonly B15WebAppFactory _factory;

    public Ts090_ProfileForeignEmailConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithForeignTakenEmail_ReturnsConflict()
    {
        // given: пользователь A (своя сессия) и пользователь B с email b@x.ru — DI-сид.
        var userA = B15Harness.SeedStudent(_factory, fullName: FullNameA, login: LoginA, email: EmailA);
        B15Harness.SeedStudent(_factory, fullName: FullNameB, login: LoginB, email: EmailB);
        using var client = B15Harness.CreateSessionClient(_factory, userA.Id);

        // when: попытка занять чужой email.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = FullNameA,
            email = EmailB,
        });

        // then: 409 с дословным текстом словаря.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Пользователь с таким email уже существует");
    }
}
