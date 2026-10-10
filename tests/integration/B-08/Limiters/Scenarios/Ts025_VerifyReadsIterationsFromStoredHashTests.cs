using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-025 «KDF: параметры Verify читаются из хранимой строки»
/// (boundary, FR-005, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000 (хранимый
///        passwordHash с сегментом '$1000$'); в рамках того же процесса
///        теста hasher пересоздан с Auth__Pbkdf2Iterations=2000 (хранилище
///        не пересоздано; хэшер читает IOptions&lt;AuthOptions&gt; на каждый
///        вызов — «пересоздание» исполнено мутацией
///        OptionsManager.Value из DI теста, механика кейса TS-026 поддерева
///        Auth/ той же зоны).
/// when:  POST /auth/login с логином пользователя и его старым правильным
///        паролем; затем регистрация нового пользователя.
/// then:  200 (Verify использует 1000 из хранимого хэша — смена
///        конфигурации не ломает старые хэши); hash нового пользователя
///        создаётся с 2000 ('pbkdf2-sha256$2000$') (AC FR-005 «Параметры из
///        хранимой строки»).
/// </summary>
public sealed class Ts025_VerifyReadsIterationsFromStoredHashTests : IClassFixture<B08LimitersKdfWebAppFactory>
{
    private const string LegacyLogin = "b08ts025legacy";
    private const string LegacyEmail = "b08ts025legacy@example.com";
    private const string FreshLogin = "b08ts025fresh";
    private const string FreshEmail = "b08ts025fresh@example.com";

    private readonly B08LimitersKdfWebAppFactory _factory;

    public Ts025_VerifyReadsIterationsFromStoredHashTests(B08LimitersKdfWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginAfterIterationsChange_VerifiesStoredHash_NewHashUsesNewIterations()
    {
        using var client = B08LimitersClients.CreateClient(_factory);

        // given: пользователь создан при Auth__Pbkdf2Iterations=1000 (хранимый
        // passwordHash с сегментом '$1000$').
        using var legacyRegister = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
        {
            fullName = "Legacy Kdf User",
            login = LegacyLogin,
            email = LegacyEmail,
            password = B08LimitersClients.TestUserPassword,
            repeatPassword = B08LimitersClients.TestUserPassword,
        });
        Assert.Equal(HttpStatusCode.Created, legacyRegister.StatusCode);
        var legacyHash = B08LimitersClients.FindUserByLogin(_factory, LegacyLogin)!.PasswordHash;
        Assert.StartsWith("pbkdf2-sha256$1000$", legacyHash, StringComparison.Ordinal);

        // given: hasher пересоздан с Auth__Pbkdf2Iterations=2000 в рамках того
        // же процесса; хранилище не пересоздано (мутация IOptions.Value из DI
        // теста — хэшер читает опции на каждый вызов Hash/Verify).
        var authOptions = _factory.Services.GetRequiredService<IOptions<AuthOptions>>();
        authOptions.Value.Pbkdf2Iterations = 2000;

        // when: POST /auth/login со старым правильным паролем; затем
        // регистрация нового пользователя.
        using var login = await B08LimitersClients.PostLoginAsync(
            client, LegacyLogin, B08LimitersClients.TestUserPassword);
        using var freshRegister = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
        {
            fullName = "Fresh Kdf User",
            login = FreshLogin,
            email = FreshEmail,
            password = B08LimitersClients.TestUserPassword,
            repeatPassword = B08LimitersClients.TestUserPassword,
        });

        // then: 200 (Verify использует 1000 из хранимого хэша); hash нового
        // пользователя создаётся с 2000 ('pbkdf2-sha256$2000$').
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Created, freshRegister.StatusCode);
        var freshHash = B08LimitersClients.FindUserByLogin(_factory, FreshLogin)!.PasswordHash;
        Assert.StartsWith("pbkdf2-sha256$2000$", freshHash, StringComparison.Ordinal);
    }
}
