using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-093 «Профиль: PUT пустое ФИО — 400» (negative, FR-015, P1).
///
/// given: пользователь авторизован (сессия — минтованный access-cookie,
///        ADR-015); email валиден.
/// when:  PUT /me/profile {fullName:'  ', email:'a@b.ru'}.
/// then:  400 'Данные заполнены неверно', errors.fullName=['Заполните поле']
///        (FR-015 AC «Пустое ФИО»; валидация fullName как во FR-006).
/// </summary>
public sealed class Ts093_ProfilePutBlankFullName400Tests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts093";
    private const string Email = "ts093@x.ru";
    private const string ValidEmail = "a@b.ru";

    private readonly B17WebAppFactory _factory;

    public Ts093_ProfilePutBlankFullName400Tests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredError()
    {
        // given: пользователь авторизован; его текущий email отличен от 'a@b.ru'.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Прежнее ФИО",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {fullName:'  ', email:'a@b.ru'}.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = "  ",
            email = ValidEmail,
        });

        // then: 400; errors.fullName = ['Заполните поле'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "fullName", ErrorTexts.Required);
    }
}
