using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-081 «me/profile PUT: email, занятый другим пользователем — 409»
/// (negative, FR-015, P0).
///
/// given: email 'other@example.com' занят другим пользователем — оба пользователя
///        созданы прямым DI-сидом в IUserRepository тестового хоста (методика
///        зоны, ADR-015/CR-001); сессия первого — cookie access_token харнеса.
/// when:  PUT /me/profile с этим email (fullName валиден).
/// then:  409 'Пользователь с таким email уже существует'
///        (FR-015 AC «Занятый email»).
/// </summary>
public sealed class Ts081_MeProfilePutTakenEmailConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts081main";
    private const string OwnEmail = "main081@example.com";
    private const string FullName = "Пользователь Основной";
    private const string ForeignLogin = "ts081other";
    private const string TakenEmail = "other@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts081_MeProfilePutTakenEmailConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithTakenForeignEmail_ReturnsConflict()
    {
        // given: пользователь с сессией и ДРУГОЙ пользователь с email other@example.com.
        var user = B15Harness.SeedStudent(_factory, fullName: FullName, login: Login, email: OwnEmail);
        B15Harness.SeedStudent(_factory, fullName: "Пользователь Чужой", login: ForeignLogin, email: TakenEmail);
        using var client = B15Harness.CreateSessionClient(_factory, user.Id);

        // when: попытка занять чужой email при валидном fullName.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = FullName,
            email = TakenEmail,
        });

        // then: 409 с дословным текстом словаря (IF-013 CONFLICT_EMAIL).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Пользователь с таким email уже существует");
    }
}
