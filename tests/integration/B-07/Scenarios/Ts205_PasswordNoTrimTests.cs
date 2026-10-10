using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-205 «Пароль без трима: регистрация, вход с дословным и триммированным
/// значением» (boundary, FR-006/FR-007, P1).
///
/// given: логин 'spuser' и email 'sp@example.com' свободны; меток login-лимитера
///        нет (свежая фикстура класса — пустое хранилище и лимитеры); пароль
///        ' Passw0rd! ' — ведущий и хвостовой пробел являются частью пароля
///        (длина без трима 11).
/// when:  POST /auth/register {fullName:'И И И', login:'spuser',
///        email:'sp@example.com', password/repeatPassword:' Passw0rd! '};
///        затем POST /auth/login {login:'spuser', password:' Passw0rd! '};
///        затем POST /auth/login {login:'spuser', password:'Passw0rd!'}
///        (триммированный).
/// then:  регистрация — 201 (пробел — легальный спецзнак [^\p{L}\d], длина 8–128
///        без трима); вход с дословным паролем — 200 MeDto; вход с триммированным
///        — 401 'Неверный логин или пароль' (FR-006: password — сырая строка БЕЗ
///        трима; FR-007: password — строка как есть, без трима).
/// </summary>
public sealed class Ts205_PasswordNoTrimTests : IClassFixture<B07AuthWebAppFactory>
{
    /// <summary>Пароль кейса: ведущий и хвостовой пробел — часть пароля (длина 11).</summary>
    private const string PaddedPassword = " Passw0rd! ";

    private readonly B07AuthWebAppFactory _factory;

    public Ts205_PasswordNoTrimTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PaddedPasswordRegisters_LiteralPasswordLoginsIn_TrimmedPasswordRejected()
    {
        // given: 'spuser'/'sp@example.com' свободны; меток login-лимитера нет
        // (свежая фикстура класса — пустое хранилище и лимитеры).
        using var client = B07AuthClients.CreateClient(_factory);

        // when: регистрация с паролем ' Passw0rd! ' (сырая строка, без трима).
        using var register = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
        {
            fullName = "И И И",
            login = "spuser",
            email = "sp@example.com",
            password = PaddedPassword,
            repeatPassword = PaddedPassword,
        });

        // then: 201 — пробел легальный спецзнак, длина 11 в границах 8–128.
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        // when: вход с дословным паролем ' Passw0rd! '.
        using var literal = await B07AuthClients.PostLoginAsync(client, "spuser", PaddedPassword);

        // then: 200 с телом MeDto зарегистрированного пользователя.
        Assert.Equal(HttpStatusCode.OK, literal.StatusCode);
        var me = await BodyAssertions.ReadRootObjectAsync(literal);
        BodyAssertions.StringPropertyIs(me, "login", "spuser");

        // when: вход с триммированным паролем 'Passw0rd!'.
        using var trimmed = await B07AuthClients.PostLoginAsync(client, "spuser", "Passw0rd!");

        // then: 401 'Неверный логин или пароль' — пароль сверяется БЕЗ трима
        // (FR-006/FR-007), триммированное значение не совпадает с сохранённым.
        Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
        var error = await BodyAssertions.ReadRootObjectAsync(trimmed);
        BodyAssertions.MessageIs(error, "Неверный логин или пароль");
    }
}
