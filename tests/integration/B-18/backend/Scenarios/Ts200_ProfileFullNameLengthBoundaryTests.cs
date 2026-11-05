using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-200 «Профиль: границы длины fullName 200/201» (boundary, FR-015, P2).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет); email валиден и не меняется
///        (собственный email — совпадение с собой не конфликт).
/// when:  PUT /api/v1/me/profile с fullName длиной ровно 200 символов; затем
///        отдельный PUT с fullName длиной 201.
/// then:  первый — 200 (ProfileDto с новым ФИО); второй — 400,
///        errors.fullName=['ФИО — от 1 до 200 символов'] (FR-015: валидация как во
///        FR-006, errors {fullName, email}; текст словаря fullName).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts200_ProfileFullNameLengthBoundaryTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string OwnEmail = "teacher@example.com";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts200_ProfileFullNameLengthBoundaryTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_FullName200Accepted_FullName201RejectedWithLengthError()
    {
        // given: текущий пользователь — засеянный преподаватель; email не меняется
        // (собственный); клиент анонимный.
        var current = await B18ProfileHarness.CurrentUserAsync(_factory);
        Assert.Equal(OwnEmail, current.Email);
        using var client = B18ProfileHarness.Create(_factory);

        var fullName200 = new string('Ф', 200);
        var fullName201 = new string('Ф', 201);
        Assert.Equal(200, fullName200.Length);
        Assert.Equal(201, fullName201.Length);

        // when/then №1: fullName длиной ровно 200 — 200, ProfileDto с новым ФИО.
        using var accepted = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = OwnEmail,
        });
        Assert.True(
            accepted.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 для fullName длиной 200, фактически {(int)accepted.StatusCode}: {await accepted.Content.ReadAsStringAsync()}");
        var acceptedRoot = await B18ProfileAssertions.ReadRootObjectAsync(accepted);
        B18ProfileAssertions.StringPropertyIs(acceptedRoot, "fullName", fullName200);

        // when/then №2: отдельный PUT с fullName длиной 201 — 400, errors.fullName дословно.
        using var rejected = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = fullName201,
            email = OwnEmail,
        });
        Assert.True(
            rejected.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для fullName длиной 201, фактически {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");
        var rejectedRoot = await B18ProfileAssertions.ReadRootObjectAsync(rejected);
        B18ProfileAssertions.MessageIs(rejectedRoot, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldEquals(rejectedRoot, "fullName", ErrorTexts.FullNameLength);
    }
}
