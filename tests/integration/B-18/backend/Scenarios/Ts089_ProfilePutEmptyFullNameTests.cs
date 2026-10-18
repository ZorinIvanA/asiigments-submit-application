using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-089 «Профиль: пустое ФИО» (negative, FR-015, P1).
///
/// given: пользователь авторизован; email валиден.
/// when:  PUT /api/v1/me/profile {fullName:'  ', email:'a@b.ru'}.
/// then:  400; message «Данные заполнены неверно»; errors.fullName=['Заполните поле']
///        (FR-015 AC «Пустое ФИО»; валидация как во FR-006, словарь required).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts089_ProfilePutEmptyFullNameTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts089";
    private const string Email = "ts089@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts089_ProfilePutEmptyFullNameTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredError()
    {
        // given: пользователь авторизован (email учётки валиден).
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-089",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с ФИО из одних пробелов и валидным email 'a@b.ru'.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = "  ",
            email = "a@b.ru",
        });

        // then: 400 «Данные заполнены неверно»; errors.fullName = ['Заполните поле'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldEquals(root, "fullName", ErrorTexts.Required);
    }
}
