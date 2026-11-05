using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-096 «Профиль: PUT некорректный формат email — 400» (negative, FR-015, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель; клиент
///        анонимный (аутентификации в приложении нет); fullName валиден.
/// when:  PUT /me/profile {fullName:'Ф', email:'abc'}.
/// then:  400 'Данные заполнены неверно', errors.email=['Введите корректный
///        email'] (валидация email как во FR-006).
/// </summary>
public sealed class Ts096_ProfilePutInvalidEmailFormatTests : IClassFixture<B17WebAppFactory>
{
    private readonly B17WebAppFactory _factory;

    public Ts096_ProfilePutInvalidEmailFormatTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithMalformedEmail_Returns400WithFormatError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        _ = await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        // when: PUT {fullName:'Ф', email:'abc'} — fullName валиден, email нет.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = "Ф",
            email = "abc",
        });

        // then: 400; errors.email = ['Введите корректный email'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.InvalidData);
        B17BodyAssertions.ErrorFieldEquals(root, "email", ErrorTexts.EmailFormat);
    }
}
