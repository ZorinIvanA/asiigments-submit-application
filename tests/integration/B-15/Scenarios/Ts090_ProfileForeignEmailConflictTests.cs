using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-090 «PUT /me/profile: чужой занятый email ci → 409» (negative, FR-021, P0).
///
/// given: пользователь B role=student с email 'b@x.ru' создан прямым DI-сидом
///        в IUserRepository тестового хоста (NFR-001: публичный API для сида
///        не используется); текущий пользователь приложения — засеянный
///        преподаватель (Seed__TeacherLogin), его email отличен от 'b@x.ru';
///        клиент анонимный (аутентификации в приложении нет).
/// when:  PUT /me/profile {fullName:текущее, email:'b@x.ru'}.
/// then:  409 «Пользователь с таким email уже существует».
///        FR-021 AC «Чужой занятый email».
/// </summary>
public sealed class Ts090_ProfileForeignEmailConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string LoginB = "ts090b";
    private const string EmailB = "b@x.ru";
    private const string FullNameB = "Пользователь Б";

    private readonly B15WebAppFactory _factory;

    public Ts090_ProfileForeignEmailConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithForeignTakenEmail_ReturnsConflict()
    {
        // given: пользователь B с email b@x.ru (DI-сид); текущий пользователь —
        // засеянный преподаватель; клиент анонимный.
        B15Harness.SeedStudent(_factory, fullName: FullNameB, login: LoginB, email: EmailB);
        var current = await B15Harness.CurrentUserAsync(_factory);
        using var client = B15Harness.CreateClient(_factory);

        // when: попытка занять чужой email.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = current.FullName,
            email = EmailB,
        });

        // then: 409 с дословным текстом словаря.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Пользователь с таким email уже существует");
    }
}
