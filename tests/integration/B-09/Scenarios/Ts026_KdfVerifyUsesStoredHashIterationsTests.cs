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
/// Механика given: шов B09KdfSeams.SetPbkdf2Iterations мутирует IOptions
/// тестового хоста — хост и хранилище не пересоздаются (дословно given).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файлы прежних волн
/// зоны с совпадающим поведением (Ts023_KdfIterationsFromStoredHash,
/// Ts026_KdfVerifyReadsStoredHashParameters) не изменялись.
/// </summary>
public sealed class Ts026_KdfVerifyUsesStoredHashIterationsTests : IClassFixture<B09WebAppFactory>
{
    private const string LegacyLogin = "ts026-legacy";
    private const string LegacyEmail = "ts026-legacy@example.com";
    private const string LegacyRegistrationIp = "10.0.0.76";

    private const string NewLogin = "ts026-new";
    private const string NewEmail = "ts026-new@example.com";
    private const string NewRegistrationIp = "10.0.0.77";

    private const string Password = "Passw0rd!";

    private readonly B09WebAppFactory _factory;

    public Ts026_KdfVerifyUsesStoredHashIterationsTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OldPasswordVerifiesFromStoredHash_NewHashCreatedWithCurrentIterations()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000...
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой Легаси", LegacyLogin, LegacyEmail, ip: LegacyRegistrationIp);
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(LegacyLogin));

        // given: ...затем в рамках теста конфигурация изменена на 2000
        // (хранилище не пересоздано — тот же хост/процесс).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        // when: вход со старым правильным паролем.
        var client = B09HostClients.Create(_factory);
        using var loginResponse = await B09HostClients.LoginAsync(client, LegacyLogin, Password);

        // when: регистрация нового пользователя при 2000.
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой Новый", NewLogin, NewEmail, ip: NewRegistrationIp);

        // then: вход — 200 (Verify использует 1000 из САМОГО хранимого хэша,
        // а не текущую конфигурацию).
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
