using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-051 «Production с пустым Auth__JwtKey не стартует» (negative, FR-008,
/// P0).
///
/// given: Environment=Production; Auth__JwtKey не задан.
/// when:  попытка старта приложения (dotnet run / WebApplicationFactory).
/// then:  старт прерван ошибкой конфигурации, процесс завершён ненулевым
///        кодом; HTTP-запросы не обслуживаются (FR-008 AC «Production без
///        ключа не стартует»).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth. Поведенческая часть кейса исполнима дословно и
/// исполнена в собственной зоне батча B-09 (прецедент c-1052); расхождение
/// размещения зафиксировано в scenario_change_requests.
///
/// Отображение given/then на тестовый процесс: старт приложения — построение и
/// запуск хоста WebApplicationFactory (ValidateOnStart валидации AuthOptions —
/// fail-fast guard); «ненулевой код выхода» процесса эквивалентен выброшенному
/// исключению конфигурации при старте хоста в процессе testhost; HTTP-запросы
/// не обслуживаются — клиент создать невозможно (CreateClient падает вместе со
/// стартом). Seed__TeacherPassword фикстуры задан валидный, чтобы отказ был
/// сконцентрирован ТОЛЬКО в Auth__JwtKey.
/// </summary>
public sealed class Ts051_ProductionEmptyJwtKeyNoStartTests
{
    [Fact]
    public void ProductionHostWithoutJwtKey_StartAbortedWithConfigurationError()
    {
        using var factory = new B09AuthProductionNoJwtKeyFactory();

        // when: попытка старта приложения (хост поднимается лениво — первым
        // обращением CreateClient; валидация конфигурации — на старте хоста).
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // then: старт прерван ошибкой конфигурации — в цепочке исключений
        // названа переменная Auth__JwtKey (обязательна в Production).
        var messages = B09AuthSupport.FlattenExceptionMessages(exception);
        Assert.Contains(AuthOptions.JwtKeyVariable, messages, StringComparison.Ordinal);

        // then: HTTP-запросы не обслуживаются — хост не построен, повторная
        // попытка получить клиента падает так же (обслуживания нет).
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
