using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-089 «PUT /me/profile: успех с тримом значений» (happy_path, FR-021, P0).
///
/// given: сессия студента (собственная учётка регистрации — свежий экземпляр
///        приложения в фикстуре класса, email «n@e.ru» никем не занят).
/// when:  PUT /api/v1/me/profile {fullName:' Новое Имя ', email:' n@e.ru '}.
/// then:  200; в ответе и хранилище fullName='Новое Имя', email='n@e.ru'
///        (значения триммятся). FR-021 AC «Успешное изменение».
/// </summary>
public sealed class Ts089_ProfileUpdateTrimTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts089";
    private const string Email = "ts089@x.ru";

    private readonly B07WebAppFactory _factory;

    public Ts089_ProfileUpdateTrimTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_TrimsValues_ReturnsAndStoresTrimmed()
    {
        // given: сессия студента; email «n@e.ru» свободен (свежее хранилище фикстуры).
        var (client, _) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: "Старое Имя", login: Login, email: Email);

        // when: изменение профиля со значениями в пробелах.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
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
        var stored = HostClients.ResolveUserByEmail(_factory, "n@e.ru");
        Assert.Equal("Новое Имя", stored.FullName);
        Assert.Equal("n@e.ru", stored.Email);
    }
}
