using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-052 «Development с пустым Auth__JwtKey: эпизодический ключ и
/// предупреждение» (happy_path, FR-008, P2).
///
/// given: Environment=Development; Auth__JwtKey не задан; тестовый log-sink
///        подключён.
/// when:  старт приложения; вход teacher/teacher123!.
/// then:  приложение стартует; в логе есть предупреждение о незаданном ключе;
///        вход — 200 (подпись эпизодическим случайным ключом работает в
///        пределах процесса) (FR-008: «в Development — эпизодический случайный
///        ключ и предупреждение в лог»).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth. Поведенческая часть кейса исполнима дословно и
/// исполнена в собственной зоне батча B-09 (прецедент c-1052); расхождение
/// размещения зафиксировано в scenario_change_requests.
/// </summary>
public sealed class Ts052_DevelopmentEphemeralJwtKeyTests
{
    [Fact]
    public async Task DevelopmentHostWithoutJwtKey_Starts_LogsWarning_LoginSucceeds()
    {
        using var factory = new B09AuthDevNoJwtKeyFactory();
        using var client = B09AuthSupport.CreateClient(factory);

        // when: старт приложения (хост построен — CreateClient вернул клиента);
        // вход teacher/teacher123! (умолчания SeedOptions: без явной переменной
        // сид-учётка получает login 'teacher', password 'teacher123!').
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", "teacher123!");

        // then: приложение стартует; в логе есть предупреждение о незаданном
        // ключе — Warning в категории «Hosting.Configuration», в записи названа
        // переменная; само значение эпизодического ключа в запись НЕ попадает
        // (NFR-006).
        var warnings = factory.LogSink
            .OfCategory(B09AuthSupport.HostingConfigurationCategory)
            .Where(record => record.Level == LogLevel.Warning)
            .ToList();
        Assert.Contains(
            warnings,
            warning => warning.Serialize().Contains("Auth__JwtKey", StringComparison.Ordinal));

        // then: вход — 200 (подпись эпизодическим случайным ключом работает в
        // пределах процесса).
        var body = await B09Assertions.ParseObjectAsync(
            login, HttpStatusCode.OK, "вход teacher/teacher123! в Development без Auth__JwtKey (TS-052)");
        Assert.Equal("teacher", body.GetProperty("login").GetString());
        Assert.Equal("teacher", body.GetProperty("role").GetString());
    }
}
