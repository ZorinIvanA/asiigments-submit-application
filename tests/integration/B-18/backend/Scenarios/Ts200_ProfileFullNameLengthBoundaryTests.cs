using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-200 «Профиль: границы длины fullName 200/201» (boundary, FR-015, P2).
///
/// given: пользователь авторизован; email валиден и свободен (не меняется —
///        собственный email, по TS-090 совпадение с собой не конфликт).
/// when:  PUT /api/v1/me/profile с fullName длиной ровно 200 символов; затем
///        отдельный PUT с fullName длиной 201.
/// then:  первый — 200 (ProfileDto с новым ФИО); второй — 400,
///        errors.fullName=['ФИО — от 1 до 200 символов'] (FR-015: валидация как во
///        FR-006, errors {fullName, email}; текст словаря fullName).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts200_ProfileFullNameLengthBoundaryTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts200";
    private const string Email = "ts200@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts200_ProfileFullNameLengthBoundaryTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_FullName200Accepted_FullName201RejectedWithLengthError()
    {
        // given: пользователь авторизован; email не меняется (собственный).
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-200",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        var fullName200 = new string('Ф', 200);
        var fullName201 = new string('Ф', 201);
        Assert.Equal(200, fullName200.Length);
        Assert.Equal(201, fullName201.Length);

        // when/then №1: fullName длиной ровно 200 — 200, ProfileDto с новым ФИО.
        using var accepted = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = fullName200,
            email = Email,
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
            email = Email,
        });
        Assert.True(
            rejected.StatusCode == HttpStatusCode.BadRequest,
            $"Ожидался статус 400 для fullName длиной 201, фактически {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");
        var rejectedRoot = await B18ProfileAssertions.ReadRootObjectAsync(rejected);
        B18ProfileAssertions.MessageIs(rejectedRoot, ErrorTexts.InvalidData);
        B18ProfileAssertions.ErrorFieldEquals(rejectedRoot, "fullName", ErrorTexts.FullNameLength);
    }
}
