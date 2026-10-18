using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-199 «Регистрация: границы длины fullName 200/201» (boundary, P2, FR-006).
///
/// given: прочие поля валидны; логин и email свободны (демо-сид логины student01..32/
///        teacher не пересекаются с кейсовыми).
/// when:  POST /api/v1/auth/register с fullName длиной ровно 200 символов;
///        затем ОТДЕЛЬНЫЙ POST с fullName длиной 201.
/// then:  первый — 201; второй — 400,
///        errors.fullName=['ФИО — от 1 до 200 символов']
///        (FR-006: «fullName 1–200 после трима»; словарь fullName).
/// </summary>
public sealed class Ts199_RegisterFullNameBoundaryTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private const string TestPassword = "Passw0rd!";

    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts199_RegisterFullNameBoundaryTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithFullName200_CreatesAnd201CharsRejected()
    {
        // when: POST /auth/register с fullName ровно 200 символов.
        using var validResponse = await ApiRequests.RegisterAsync(
            _factory.CreateClient(),
            fullName: new string('Ф', 200),
            login: "ts199-full200",
            email: "ts199-full200@example.com",
            password: TestPassword,
            repeatPassword: TestPassword);

        // then: 201.
        Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);

        // when: отдельный POST /auth/register с fullName 201 символ (логин/email свободны).
        using var tooLongResponse = await ApiRequests.RegisterAsync(
            _factory.CreateClient(),
            fullName: new string('Ф', 201),
            login: "ts199-full201",
            email: "ts199-full201@example.com",
            password: TestPassword,
            repeatPassword: TestPassword);

        // then: 400, errors.fullName=['ФИО — от 1 до 200 символов'].
        Assert.Equal(HttpStatusCode.BadRequest, tooLongResponse.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(tooLongResponse);
        BodyAssertions.ErrorFieldIsExactly(root, "fullName", "ФИО — от 1 до 200 символов");
    }
}
