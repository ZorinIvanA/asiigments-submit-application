using LabsApp.IntegrationTests.B03.Infrastructure;

namespace LabsApp.IntegrationTests.B03.Scenarios;

/// <summary>
/// TS-033 «Регистрация: дубликат логина проверяется раньше email» (FR-006 AC
/// «Дубликат логина проверяется первым»; P0): given — существуют login 'stu' и
/// email 'stu@example.com' (один пользователь, DI-сид); when — POST /auth/register
/// {login:'STU', email:'stu@example.com', …валидные прочие…}; then — 409, message
/// 'Пользователь с таким логином уже существует' — без проверки email.
/// </summary>
public sealed class Ts033_DuplicateLoginFirstTests : IClassFixture<B03HostFactory>
{
    private readonly B03HostFactory _factory;

    public Ts033_DuplicateLoginFirstTests(B03HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_DuplicateLoginCaseInsensitive_ReturnsLoginConflictWithoutEmailCheck()
    {
        // given: существуют пользователь с login 'stu' и email 'stu@example.com'.
        B03UserSeed.AddStudent(_factory, login: "stu", email: "stu@example.com");
        using var client = B03RegisterApi.CreateClient(_factory, remoteIp: "10.33.0.1");

        // when: регистрация с тем же логином в другом регистре и тем же email.
        using var response = await B03RegisterApi.PostAsync(
            client,
            fullName: "Ф И О",
            login: "STU",
            email: "stu@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 409 'Пользователь с таким логином уже существует' — конфликт логина
        // обнаружен раньше email (иначе message был бы текстом конфликта email).
        using var body = await ResponseAssert.ParseWithStatusAsync(
            response,
            HttpStatusCode.Conflict,
            "POST /api/v1/auth/register (дубликат логина 'STU' при существующем 'stu')");
        ResponseAssert.MessageIs(body.RootElement, "Пользователь с таким логином уже существует");
    }
}
