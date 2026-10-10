using LabsApp.IntegrationTests.B09.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-026 «KDF-сервис: параметры Verify читаются из хранимого хэша»
/// (boundary, FR-005, P0).
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
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (файл Ts026_KdfVerifyReadsStoredHashParametersTests.cs;
/// копия Ts023_KdfIterationsFromStoredHash в B-09 помечена арбитражем
/// дубликатом). Поведенческая часть кейса исполнима дословно и исполнена в
/// собственной зоне батча B-09 (прецедент c-1052); расхождение размещения
/// зафиксировано в scenario_change_requests. Существующие файлы прежней волны
/// зоны (Ts023*) не изменялись.
///
/// Механика given: шов B09KdfSeams.SetPbkdf2Iterations мутирует IOptions
/// тестового хоста — хост и хранилище не пересоздаются (дословно given).
/// </summary>
public sealed class Ts026_KdfVerifyReadsStoredHashParametersTests : IClassFixture<B09WebAppFactory>
{
    private const string LegacyLogin = "ts026legacy";
    private const string LegacyEmail = "ts026legacy@example.com";
    private const string LegacyRegistrationIp = "10.0.0.26";

    private const string NewLogin = "ts026fresh";
    private const string NewEmail = "ts026fresh@example.com";
    private const string NewRegistrationIp = "10.0.0.62";

    private const string Password = "Passw0rd!";

    private readonly B09WebAppFactory _factory;

    public Ts026_KdfVerifyReadsStoredHashParametersTests(B09WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginAfterIterationsChange_VerifiesFromStoredHash_NewHashUsesNewIterations()
    {
        // given: пользователь создан при Auth__Pbkdf2Iterations=1000...
        B09KdfSeams.SetPbkdf2Iterations(_factory, 1000);
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой", LegacyLogin, LegacyEmail, ip: LegacyRegistrationIp);
        Assert.NotNull(_factory.Services.GetRequiredService<IUserRepository>().GetByLogin(LegacyLogin));

        // given: ...в рамках того же процесса конфигурация изменена на 2000
        // (хранилище не пересоздано — тот же хост).
        B09KdfSeams.SetPbkdf2Iterations(_factory, 2000);

        // when: POST /auth/login со старым правильным паролем.
        var client = B09HostClients.Create(_factory);
        using var loginResponse = await B09HostClients.LoginAsync(client, LegacyLogin, Password);

        // when: регистрация нового пользователя при 2000.
        await B09HostClients.RegisterStudentAsync(
            _factory, "Двадцать Шестой Новый", NewLogin, NewEmail, ip: NewRegistrationIp);

        // then: вход — 200 (Verify использует 1000 из хранимого хэша).
        _ = await B09Assertions.ParseObjectAsync(
            loginResponse,
            HttpStatusCode.OK,
            "вход со старым правильным паролем (хэш 1000, конфигурация 2000)");

        // then: хэш нового пользователя начинается с 'pbkdf2-sha256$2000$'.
        var newHash = B09HostClients.ResolveUserByEmail(_factory, NewEmail).PasswordHash;
        Assert.True(
            newHash.StartsWith("pbkdf2-sha256$2000$", StringComparison.Ordinal),
            "Хэш нового пользователя должен создаваться с итерациями текущей конфигурации (2000): " +
            $"префикс 'pbkdf2-sha256$2000$', фактически: {newHash}");
    }
}
