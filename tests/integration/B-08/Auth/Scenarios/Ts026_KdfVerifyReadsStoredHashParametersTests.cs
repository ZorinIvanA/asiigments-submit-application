using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-026 «KDF-сервис: параметры Verify читаются из хранимого хэша»
/// (boundary, FR-005, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; в рамках одного
///        процесса теста конфигурация изменена на 2000 (хранилище не
///        пересоздано).
/// when:  POST /auth/login со старым правильным паролем; затем регистрация
///        нового пользователя.
/// then:  вход — 200 (Verify использует 1000 из хранимого хэша); хэш нового
///        пользователя начинается с 'pbkdf2-sha256$2000$' (FR-005 AC
///        «Параметры из хранимой строки»).
///
/// Механика given: хэшер читает Auth__Pbkdf2Iterations из IOptions на каждый
/// вызов Hash (IF-002), поэтому «изменение конфигурации в рамках процесса» —
/// мутация OptionsManager.Value из DI теста; хост и хранилище не
/// пересоздаются.
/// </summary>
public sealed class Ts026_KdfVerifyReadsStoredHashParametersTests
{
    private const string LegacyLogin = "legacykdf";
    private const string LegacyEmail = "legacykdf@example.com";
    private const string NewLogin = "freshkdf";
    private const string NewEmail = "freshkdf@example.com";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task LoginAfterIterationsChange_VerifiesFromStoredHash_NewHashUsesNewIterations()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: пользователь создан при Auth__Pbkdf2Iterations=1000 (регистрация
        // через HTTP; фикстура задаёт 1000 — предусловие given проверяем по хэшу).
        using var legacyRegister = await client.PostAsync(
            B08AuthHost.RegisterEndpoint,
            RegisterBody("Легаси Пользователь", LegacyLogin, LegacyEmail, Password));
        B08AuthHost.AssertStatus(legacyRegister, HttpStatusCode.Created, "регистрация пользователя при 1000 итерациях");

        var users = factory.Services.GetRequiredService<LabsApp.Storage.IUserRepository>();
        var legacyHash = users.GetByLogin(LegacyLogin)!.PasswordHash;
        Assert.StartsWith("pbkdf2-sha256$1000$", legacyHash);

        // given: конфигурация изменена на 2000 в рамках одного процесса;
        // хранилище не пересоздано (мутация IOptions.Value из DI теста).
        var authOptions = factory.Services.GetRequiredService<IOptions<AuthOptions>>();
        authOptions.Value.Pbkdf2Iterations = 2000;

        // when: вход со старым правильным паролем.
        using var login = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + LegacyLogin + "\",\"password\":\"" + Password + "\"}");

        // when: регистрация нового пользователя (Hash — уже с новой конфигурацией).
        using var newRegister = await client.PostAsync(
            B08AuthHost.RegisterEndpoint,
            RegisterBody("Свежий Пользователь", NewLogin, NewEmail, Password));

        // then: вход — 200 (Verify использует 1000 из хранимого хэша);
        // хэш нового пользователя начинается с 'pbkdf2-sha256$2000$'.
        B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "вход со старым паролем после смены итераций");
        var loginBody = await B08AuthHost.ReadJsonObjectAsync(login, "тело 200 входа (TS-026)");
        Assert.Equal(LegacyLogin, B08AuthHost.StringProperty(loginBody, "login"));

        B08AuthHost.AssertStatus(newRegister, HttpStatusCode.Created, "регистрация нового пользователя при 2000 итерациях");
        var newHash = users.GetByLogin(NewLogin)!.PasswordHash;
        Assert.StartsWith("pbkdf2-sha256$2000$", newHash);

        // Проверка структуры хэша-результата (детерминизм формата IF-002: ровно 4 сегмента).
        Assert.Equal(4, newHash.Split('$').Length);
    }

    private static string RegisterBody(string fullName, string login, string email, string password) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["fullName"] = fullName,
            ["login"] = login,
            ["email"] = email,
            ["password"] = password,
            ["repeatPassword"] = password,
        });
}
