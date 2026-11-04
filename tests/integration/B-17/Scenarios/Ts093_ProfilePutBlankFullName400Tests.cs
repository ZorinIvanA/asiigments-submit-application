using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-093 «Профиль: PUT пустое ФИО — 400» (negative, FR-015, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель; клиент
///        анонимный (аутентификации в приложении нет); email валиден.
/// when:  PUT /me/profile {fullName:'  ', email:'a@b.ru'}.
/// then:  400 'Данные заполнены неверно', errors.fullName=['Заполните поле']
///        (FR-015 AC «Пустое ФИО»; валидация fullName как во FR-006).
/// </summary>
public sealed class Ts093_ProfilePutBlankFullName400Tests : IClassFixture<B17WebAppFactory>
{
    private const string ValidEmail = "a@b.ru";

    private readonly B17WebAppFactory _factory;

    public Ts093_ProfilePutBlankFullName400Tests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        _ = await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

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
