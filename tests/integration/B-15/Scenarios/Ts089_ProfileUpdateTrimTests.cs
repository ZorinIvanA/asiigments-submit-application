using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-089 «PUT /me/profile: успех с тримом значений» (happy_path, FR-021, P0).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет); email 'n@e.ru' свободен
///        (свежее хранилище фикстуры класса).
/// when:  PUT /api/v1/me/profile {fullName:' Новое Имя ', email:' n@e.ru '}.
/// then:  200; в ответе и хранилище fullName='Новое Имя', email='n@e.ru'
///        (значения триммятся). FR-021 AC «Успешное изменение».
/// </summary>
public sealed class Ts089_ProfileUpdateTrimTests : IClassFixture<B15WebAppFactory>
{
    private const string NewFullName = "Новое Имя";
    private const string NewEmail = "n@e.ru";

    private readonly B15WebAppFactory _factory;

    public Ts089_ProfileUpdateTrimTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_TrimsValues_ReturnsAndStoresTrimmed()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        var current = await B15Harness.CurrentUserAsync(_factory);
        using var client = B15Harness.CreateClient(_factory);

        // when: изменение профиля со значениями в пробелах.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = $" {NewFullName} ",
            email = $" {NewEmail} ",
        });

        // then: HTTP 200, в ответе значения без пробелов.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(root, "fullName", NewFullName);
        BodyAssertions.StringPropertyIs(root, "email", NewEmail);

        // then: и в хранилище значения триммяты (контракт IF-015: чтение — копия-снимок).
        var stored = await B15Harness.UserByEmailAsync(_factory, NewEmail);
        Assert.Equal(current.Id, stored.Id);
        Assert.Equal(NewFullName, stored.FullName);
        Assert.Equal(NewEmail, stored.Email);
    }
}
