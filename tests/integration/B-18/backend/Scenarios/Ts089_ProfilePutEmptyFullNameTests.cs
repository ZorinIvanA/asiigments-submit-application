using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-089 «Профиль: пустое ФИО» (negative, FR-015, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель; клиент
///        анонимный (аутентификации в приложении нет); email валиден.
/// when:  PUT /api/v1/me/profile {fullName:'  ', email:'a@b.ru'}.
/// then:  400; message «Данные заполнены неверно»; errors.fullName=['Заполните поле']
///        (FR-015 AC «Пустое ФИО»; валидация как во FR-006, словарь required).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts089_ProfilePutEmptyFullNameTests : IClassFixture<B18ProfileWebAppFactory>
{
    private readonly B18ProfileWebAppFactory _factory;

    public Ts089_ProfilePutEmptyFullNameTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        _ = await B18ProfileHarness.CurrentUserAsync(_factory);
        using var client = B18ProfileHarness.Create(_factory);

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
