using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-169 «Пароль не триммится: краевые пробелы значимы при регистрации и смене»
/// (boundary, FR-007, FR-012).
///
/// given: свежий экземпляр (собственная фикстура класса: 1 попытка регистрации;
///        неуспешных входов на пару — максимум 2, порог 5/мин не достигнут);
///        свободные login 'spaceman' и email; пароль-эталон ' Passw0rd! '
///        (ведущий и ведомый пробел: 11 символов — буква, цифра, спецзнак).
/// when:  регистрация с паролем ' Passw0rd! '; вход с точным значением; вход с
///        триммированным 'Passw0rd!'; смена пароля (с cookie регистрации) на
///        ' Newpass1! '; вход с точным новым значением; вход с триммированным.
/// then:  регистрация — 201; вход с ' Passw0rd! ' — 200; вход с 'Passw0rd!' —
///        401 «Неверный логин или пароль»; смена — 200; вход с ' Newpass1! ' —
///        200; вход с 'Newpass1!' — 401. FR-007: «password — правила §8 …;
///        не триммится»; те же §8-правила для нового пароля (FR-012).
/// </summary>
public sealed class Ts169_PasswordNotTrimmedTests : IClassFixture<B05WebAppFactory>
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string PasswordEndpoint = "/api/v1/me/password";
    private const string SpacedPassword = " Passw0rd! ";
    private const string TrimmedPassword = "Passw0rd!";
    private const string SpacedNewPassword = " Newpass1! ";
    private const string TrimmedNewPassword = "Newpass1!";

    private readonly B05WebAppFactory _factory;

    public Ts169_PasswordNotTrimmedTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PasswordSpacesAreSignificant_AtRegistrationLoginAndPasswordChange()
    {
        // given: регистрация с паролем ' Passw0rd! ' (точное значение с пробелами).
        using var registerClient = HostClients.Create(_factory);
        using var registration = await registerClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Пробельный Юзер",
            login = "spaceman",
            email = "spaceman@example.com",
            password = SpacedPassword,
            repeatPassword = SpacedPassword,
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        // when/then: вход с точным значением ' Passw0rd! ' — 200.
        using var exactLoginClient = HostClients.Create(_factory);
        using var exactLogin = await exactLoginClient.PostAsJsonAsync(LoginEndpoint, new
        {
            login = "spaceman",
            password = SpacedPassword,
        });
        Assert.Equal(HttpStatusCode.OK, exactLogin.StatusCode);

        // when/then: вход с триммированным 'Passw0rd!' — 401 «Неверный логин или пароль».
        using var trimmedLoginClient = HostClients.Create(_factory);
        using var trimmedLogin = await trimmedLoginClient.PostAsJsonAsync(LoginEndpoint, new
        {
            login = "spaceman",
            password = TrimmedPassword,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, trimmedLogin.StatusCode);
        var trimmedLoginBody = await BodyAssertions.ReadRootObjectAsync(trimmedLogin);
        BodyAssertions.MessageIs(trimmedLoginBody, "Неверный логин или пароль");

        // when/then: смена пароля с cookie регистрации: ' Passw0rd! ' → ' Newpass1! ' —
        // 204 (me/password отвечает 204 без тела — ADR-010/ASM-016).
        using var change = await registerClient.PutAsJsonAsync(PasswordEndpoint, new
        {
            currentPassword = SpacedPassword,
            password = SpacedNewPassword,
            confirmPassword = SpacedNewPassword,
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // when/then: вход с точным новым значением ' Newpass1! ' — 200.
        using var exactNewLoginClient = HostClients.Create(_factory);
        using var exactNewLogin = await exactNewLoginClient.PostAsJsonAsync(LoginEndpoint, new
        {
            login = "spaceman",
            password = SpacedNewPassword,
        });
        Assert.Equal(HttpStatusCode.OK, exactNewLogin.StatusCode);

        // when/then: вход с триммированным 'Newpass1!' — 401 «Неверный логин или пароль».
        using var trimmedNewLoginClient = HostClients.Create(_factory);
        using var trimmedNewLogin = await trimmedNewLoginClient.PostAsJsonAsync(LoginEndpoint, new
        {
            login = "spaceman",
            password = TrimmedNewPassword,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, trimmedNewLogin.StatusCode);
        var trimmedNewLoginBody = await BodyAssertions.ReadRootObjectAsync(trimmedNewLogin);
        BodyAssertions.MessageIs(trimmedNewLoginBody, "Неверный логин или пароль");
    }
}
