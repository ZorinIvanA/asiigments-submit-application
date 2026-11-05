using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-082 «me/profile PUT: пробельное ФИО и собственный email» (boundary,
/// FR-015, P1) — под-сценарий 1 из 2 (второй — Ts082_ProfilePutOwnEmailNoConflictTests
/// в соседнем файле; у каждого под-сценария СВОЯ фикстура).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher»); клиент анонимный (аутентификации
///        в приложении нет).
/// when:  PUT {fullName:'  ', email:'a@b.ru'}.
/// then:  400, errors.fullName=['Заполните поле'] (FR-015 AC «Пустое ФИО»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts082_ProfilePutBlankFullNameTests : IClassFixture<B16WebAppFactory>
{
    private const string ValidEmail = "a@b.ru";

    private readonly B16WebAppFactory _factory;

    public Ts082_ProfilePutBlankFullNameTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithWhitespaceFullName_Returns400WithRequiredError()
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

        // then: 400; errors.fullName = ['Заполните поле'] дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.InvalidData);
        B16Assertions.ErrorFieldEquals(root, "fullName", ErrorTexts.Required);
    }
}
