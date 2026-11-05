using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-051 «Production с пустым Auth__JwtKey не стартует» (negative, FR-008,
/// P0).
///
/// given: Environment=Production; Auth__JwtKey не задан.
/// when:  попытка старта приложения.
/// then:  старт прерван ошибкой конфигурации, процесс завершён ненулевым
///        кодом; HTTP-запросы не обслуживаются (FR-008 AC «Production без
///        ключа не стартует»).
///
/// Отображение given/then на тестовый процесс: старт приложения — построение и
/// запуск хоста WebApplicationFactory (ValidateOnStart AuthOptionsValidator —
/// fail-fast guard FR-006); «ненулевой код выхода» процесса эквивалентен
/// выброшенному исключению конфигурации при старте хоста в процессе testhost;
/// HTTP-запросы не обслуживаются — клиент создать невозможно (CreateClient
/// падает вместе со стартом). Seed__TeacherPassword фикстуры задан валидный,
/// чтобы отказ был сконцентрирован ТОЛЬКО в Auth__JwtKey.
/// </summary>
public sealed class Ts051_ProductionEmptyJwtKeyNoStartTests
{
    [Fact]
    public void ProductionHostWithoutJwtKey_StartAbortedWithConfigurationError()
    {
        using var factory = new B08AuthProductionNoJwtKeyFactory();

        // when: попытка старта приложения (хост поднимается лениво — первым
        // обращением CreateClient; ValidateOnStart валидирует AuthOptions).
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // then: старт прерван ошибкой конфигурации — в цепочке исключений
        // названа переменная Auth__JwtKey (обязательна в Production).
        var messages = B08AuthHost.FlattenExceptionMessages(exception);
        Assert.Contains(AuthOptions.JwtKeyVariable, messages, StringComparison.Ordinal);

        // then: HTTP-запросы не обслуживаются — хост не построен, повторная
        // попытка получить клиента падает так же (обслуживания нет).
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
