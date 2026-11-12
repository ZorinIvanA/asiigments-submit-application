using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

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
/// </summary>
public sealed class Ts052_DevelopmentEphemeralJwtKeyTests
{
    [Fact]
    public async Task DevelopmentHostWithoutJwtKey_Starts_LogsWarning_LoginSucceeds()
    {
        using var factory = new B08AuthDevNoJwtKeyFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // when: старт приложения (хост построен — CreateClient вернул клиента);
        // вход teacher/teacher123! (умолчания SeedOptions: без явной переменной
        // сид-учётка получает login 'teacher', password 'teacher123!').
        using var login = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"teacher\",\"password\":\"teacher123!\"}");

        // then: приложение стартует и обслуживает хост; в логе есть
        // предупреждение о незаданном ключе — Warning в категории
        // «Hosting.Configuration», в записи названа переменная; само значение
        // эпизодического ключа в запись НЕ попадает (NFR-006).
        var warnings = factory.LogSink
            .OfCategory(B08AuthHost.HostingConfigurationCategory)
            .Where(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .ToList();
        Assert.Contains(
            warnings,
            warning => warning.Serialize().Contains("Auth__JwtKey", StringComparison.Ordinal));

        // then: вход — 200 (подпись эпизодическим случайным ключом работает в
        // пределах процесса).
        B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "вход teacher/teacher123! в Development без Auth__JwtKey (TS-052)");
        var loginBody = await B08AuthHost.ReadJsonObjectAsync(login, "тело 200 входа (TS-052)");
        Assert.Equal("teacher", B08AuthHost.StringProperty(loginBody, "login"));
        Assert.Equal("teacher", B08AuthHost.StringProperty(loginBody, "role"));
    }
}
