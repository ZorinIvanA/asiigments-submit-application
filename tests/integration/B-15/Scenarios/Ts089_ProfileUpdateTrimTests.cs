using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-089 «PUT /me/profile: успех с тримом значений» (happy_path, FR-021, P0).
///
/// given: пользователь role=student с текущими fullName/email создан прямым DI-сидом
///        в IUserRepository тестового хоста (методика NFR-001/FR-007: публичный API
///        для сида не используется); uuid пользователя известен харнесу; сессия —
///        cookie access_token с JWT HS256, минтым харнесом ключом Auth__JwtKey
///        тестового хоста; POST /auth/register и POST /auth/login в предусловиях
///        НЕ вызываются. Email 'n@e.ru' свободен (свежее хранилище фикстуры класса).
/// when:  PUT /api/v1/me/profile {fullName:' Новое Имя ', email:' n@e.ru '}.
/// then:  200; в ответе и хранилище fullName='Новое Имя', email='n@e.ru'
///        (значения триммятся). FR-021 AC «Успешное изменение».
/// </summary>
public sealed class Ts089_ProfileUpdateTrimTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts089";
    private const string CurrentEmail = "ts089@x.ru";
    private const string CurrentFullName = "Старое Имя";

    private readonly B15WebAppFactory _factory;

    public Ts089_ProfileUpdateTrimTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_TrimsValues_ReturnsAndStoresTrimmed()
    {
        // given: студент создан DI-сидом; сессия — минтованный access-cookie.
        var seeded = B15Harness.SeedStudent(_factory, fullName: CurrentFullName, login: Login, email: CurrentEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // when: изменение профиля со значениями в пробелах.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = " Новое Имя ",
            email = " n@e.ru ",
        });

        // then: HTTP 200, в ответе значения без пробелов.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(root, "fullName", "Новое Имя");
        BodyAssertions.StringPropertyIs(root, "email", "n@e.ru");

        // then: и в хранилище значения триммяты (контракт IF-015: чтение — копия-снимок).
        var stored = B15Harness.UserByEmail(_factory, "n@e.ru");
        Assert.Equal(seeded.Id, stored.Id);
        Assert.Equal("Новое Имя", stored.FullName);
        Assert.Equal("n@e.ru", stored.Email);
    }
}
