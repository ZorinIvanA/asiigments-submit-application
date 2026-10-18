using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-092 «Профиль: некорректный формат email — 400» (negative, FR-015, P2).
///
/// given: пользователь авторизован; fullName валиден.
/// when:  PUT /api/v1/me/profile {fullName:'Ф И О', email:'a b@example.com'}
///        (пробел в локальной части).
/// then:  400; errors.email=['Введите корректный email'] (валидация как во FR-006,
///        словарь email — формат).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts092_ProfilePutInvalidEmailFormatTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts092";
    private const string Email = "ts092@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts092_ProfilePutInvalidEmailFormatTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithSpaceInEmailLocalPart_Returns400WithFormatError()
    {
        // given: пользователь авторизован.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-092",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с валидным ФИО и email с пробелом в локальной части.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = "Ф И О",
            email = "a b@example.com",
        });

        // then: 400 «Данные заполнены неверно»; errors.email = ['Введите корректный email'].
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldEquals(root, "email", ErrorTexts.EmailFormat);
    }
}
