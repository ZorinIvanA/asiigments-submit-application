using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-089 «Профиль: свой email не конфликтует» (boundary, FR-015, P1;
/// нумерация текущего батча B-16).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'teacher@example.com'}
///        (без изменения email).
/// then:  200 — совпадение с самим собой не 409 (AC FR-015 «Свой email не
///        конфликтует»; IF-013 CONFLICT_EMAIL: дубликат среди ДРУГИХ).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts089_ProfilePutOwnEmailNoConflictTests : IClassFixture<B16WebAppFactory>
{
    private const string OwnEmail = "teacher@example.com";
    private const string NewFullName = "Новое ФИО";

    private readonly B16WebAppFactory _factory;

    public Ts089_ProfilePutOwnEmailNoConflictTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_Returns200_Not409()
    {
        // given: текущий email пользователя — 'teacher@example.com'.
        var current = await B16Harness.CurrentUserAsync(_factory);
        Assert.Equal(OwnEmail, current.Email);
        using var client = B16Harness.Create(_factory);

        // when: PUT {fullName:'Новое ФИО', email:'teacher@example.com'} (без изменения).
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = OwnEmail,
        });

        // then: 200 — совпадение с самим собой не 409; обновлённый ProfileDto.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 (свой email не конфликтует), фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "fullName", NewFullName);
        B16Assertions.StringPropertyIs(root, "email", OwnEmail);
    }
}
