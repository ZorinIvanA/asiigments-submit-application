using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-026 «Verify читает параметры из САМОГО хранимого хэша»
/// (boundary, FR-005, P1).
///
/// given: пользователь создан при Auth__Pbkdf2Iterations=1000; затем в рамках
///        теста конфигурация сервиса изменена на 2000 (хранилище не пересоздано).
/// when:  вход со старым правильным паролем; затем регистрация нового
///        пользователя.
/// then:  вход — 200 (Verify использует 1000 из хэша); новый хэш создаётся
///        с 2000 — префикс 'pbkdf2-sha256$2000$' (FR-005 AC «Параметры из
///        хранимой строки»).
///
/// Механика given: шов B09KdfSeams.SetPbkdf2Iterations мутирует опции тестового
/// хоста — хост и хранилище не пересоздаются (дословно given).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts026_VerifyIterationsFromStoredHashStringTests : IClassFixture<B09WebAppFactory>
{
    private const string LegacyLogin = "ts026-wave-legacy";
    private const string LegacyEmail = "ts026-wave-legacy@example.com";
    private const string LegacyRegistrationIp = "10.90.0.261";

    private const string NewLogin = "ts026-wave-new";
    private const string NewEmail = "ts026-wave-new@example.com";
    private const string NewRegistrationIp = "10.90.0.262";

    private const string Password = "Passw0rd!";

    private readonly B09WebAppFactory _factory;

    public Ts026_VerifyIterationsFromStoredHashStringTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OldHashVerifiesWithStoredIterations_NewHashUsesCurrentConfiguration()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000...
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой Волна Легаси", LegacyLogin, LegacyEmail, ip: LegacyRegistrationIp);
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(LegacyLogin));

        // given: ...затем в рамках теста конфигурация изменена на 2000
        // (хранилище не пересоздано — тот же хост/процесс).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        // when: вход со старым правильным паролем.
        using var client = B09HostClients.Create(_factory);
        using var loginResponse = await B09HostClients.LoginAsync(client, LegacyLogin, Password);

        // when: регистрация нового пользователя (хэшируется текущими 2000).
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой Волна Новый", NewLogin, NewEmail, ip: NewRegistrationIp);

        // then: вход — 200 (Verify использует 1000 из САМОГО хранимого хэша).
        _ = await B09Assertions.ParseObjectAsync(
            loginResponse,
            HttpStatusCode.OK,
            "вход со старым правильным паролем (хэш 1000, конфигурация 2000)");

        // then: новый хэш создаётся с 2000 — префикс 'pbkdf2-sha256$2000$'.
        var newHash = B09HostClients.ResolveUserByEmail(_factory, NewEmail).PasswordHash;
        Assert.True(
            newHash.StartsWith("pbkdf2-sha256$2000$", StringComparison.Ordinal),
            "Новый хэш должен создаваться с итерациями текущей конфигурации (2000): " +
            $"префикс 'pbkdf2-sha256$2000$', фактически: {newHash}");
    }
}
