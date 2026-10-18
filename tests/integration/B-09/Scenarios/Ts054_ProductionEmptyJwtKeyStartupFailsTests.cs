using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-054 «Токены: Production с пустым Auth__JwtKey не стартует» (negative,
/// FR-008 + SEC-001, P0).
///
/// given: ASPNETCORE_ENVIRONMENT=Production; Auth__JwtKey не задан (пуст).
/// when:  попытка старта приложения (dotnet run / WebApplicationFactory).
/// then:  старт прерван ошибкой конфигурации; процесс завершён ненулевым
///        кодом (хост сообщает ошибку валидации конфигурации); HTTP-запросы
///        не обслуживаются (AC FR-008 «Production без ключа не стартует»).
///
/// Отображение на тестовый процесс: старт приложения — построение и запуск
/// хоста WebApplicationFactory (ValidateOnStart — fail-fast валидация
/// AuthOptions); «ненулевой код выхода» процесса эквивалентен выброшенному
/// исключению конфигурации при старте хоста в процессе testhost; HTTP-запросы
/// не обслуживаются — повторное получение клиента падает так же.
/// Seed__TeacherPassword фикстуры задан валидный НЕстандартный — отказ
/// сконцентрирован ТОЛЬКО в Auth__JwtKey. Файл текущей волны батча B-09
/// (перенумерация кейсов): файл прежней волны (Ts051_ProductionEmptyJwtKeyNoStart)
/// не изменялся.
/// </summary>
public sealed class Ts054_ProductionEmptyJwtKeyStartupFailsTests
{
    [Fact]
    public void ProductionHostWithoutJwtKey_StartAbortedWithConfigurationError_NoHttpServing()
    {
        // given: ASPNETCORE_ENVIRONMENT=Production; Auth__JwtKey не задан.
        using var factory = new B09AuthProductionNoJwtKeyFactory();

        // when: попытка старта приложения (хост поднимается лениво — первым
        // обращением CreateClient; валидация конфигурации — на старте хоста).
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // then: старт прерван ошибкой конфигурации — хост сообщает ошибку
        // валидации: в цепочке исключений названа переменная Auth__JwtKey.
        var messages = B09AuthSupport.FlattenExceptionMessages(exception);
        Assert.Contains(AuthOptions.JwtKeyVariable, messages, StringComparison.Ordinal);

        // then: HTTP-запросы не обслуживаются — хост не построен, повторная
        // попытка получить клиента падает так же.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
