using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-023 «KDF: параметры Verify читаются из хранимой строки» (boundary, FR-005, P0).
///
/// given: в рамках одного процесса теста пользователь создан при
///        Auth__Pbkdf2Iterations=1000, затем конфигурация изменена на 2000
///        (хранилище не пересоздано).
/// when:  POST /auth/login со старым правильным паролем; затем регистрация
///        нового пользователя.
/// then:  вход — 200 (Verify использует 1000 итераций из хэша); новый хэш
///        создаётся с 2000 итераций — префикс 'pbkdf2-sha256$2000$'
///        (FR-005 AC «Параметры из хранимой строки»).
/// </summary>
public sealed class Ts023_KdfIterationsFromStoredHashTests : IClassFixture<B09WebAppFactory>
{
    private const string Login = "ts023";
    private const string Email = "ts023@b.ru";
    private const string RegistrationIp = "10.0.0.23";

    private const string NewLogin = "ts023new";
    private const string NewEmail = "ts023new@b.ru";
    private const string NewRegistrationIp = "10.0.0.24";

    private readonly B09WebAppFactory _factory;

    public Ts023_KdfIterationsFromStoredHashTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OldPasswordVerifiesFromHash_NewHashesUseCurrentIterations()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000...
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Третий", Login, Email, ip: RegistrationIp);
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(Login));

        // given: ...затем конфигурация изменена на 2000 (хранилище не пересоздано —
        // тот же хост/процесс).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        // when: POST /auth/login со старым правильным паролем.
        var client = B09HostClients.Create(_factory);
        using var loginResponse = await B09HostClients.LoginAsync(
            client, Login, B09HostClients.TestUserPassword);

        // when: регистрация нового пользователя при 2000.
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Третий Новый", NewLogin, NewEmail, ip: NewRegistrationIp);

        // then: вход — 200 (Verify использует итерации ИЗ хранимого хэша, а не из
        // текущей конфигурации).
        _ = await B09Assertions.ParseObjectAsync(
            loginResponse,
            HttpStatusCode.OK,
            "вход со старым правильным паролем (хэш 1000, конфигурация 2000)");

        // then: новый хэш создаётся с 2000 итераций — префикс 'pbkdf2-sha256$2000$'.
        var newHash = B09HostClients.ResolveUserByEmail(_factory, NewEmail).PasswordHash;
        Assert.True(
            newHash.StartsWith("pbkdf2-sha256$2000$", StringComparison.Ordinal),
            "Новый хэш должен создаваться с итерациями текущей конфигурации (2000): " +
            $"префикс 'pbkdf2-sha256$2000$', фактически: {newHash}");
    }
}
