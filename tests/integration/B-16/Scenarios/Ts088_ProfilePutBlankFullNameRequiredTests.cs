using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-088 «Профиль: пустое ФИО — 400 required» (negative, FR-015, P1;
/// нумерация текущего батча B-16).
///
/// given: текущий пользователь приложения — засеянный преподаватель; клиент
///        анонимный (аутентификации в приложении нет); email валиден.
/// when:  PUT /api/v1/me/profile {fullName:'  ', email:'a@b.ru'}.
/// then:  400, errors.fullName=['Заполните поле'] (AC FR-015 «Пустое ФИО»;
///        IF-013 VALIDATION).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts088_ProfilePutBlankFullNameRequiredTests : IClassFixture<B16WebAppFactory>
{
    private const string ValidEmail = "a@b.ru";

    private readonly B16WebAppFactory _factory;

    public Ts088_ProfilePutBlankFullNameRequiredTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredFieldError()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный;
        // email 'a@b.ru' валиден и свободен.
        _ = await B16Harness.CurrentUserAsync(_factory);
        using var client = B16Harness.Create(_factory);

        // when: PUT {fullName:'  ', email:'a@b.ru'}.
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = "  ",
            email = ValidEmail,
        });

        // then: 400; errors.fullName = ['Заполните поле'] — ровно этот текст.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.InvalidData);
        B16Assertions.ErrorFieldEquals(root, "fullName", ErrorTexts.Required);
    }
}
