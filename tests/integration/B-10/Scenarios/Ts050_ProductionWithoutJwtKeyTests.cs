using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-050 «Production без Auth__JwtKey не стартует» (negative, FR-008, P0).
///
/// given: ASPNETCORE_ENVIRONMENT=Production; Auth__JwtKey не задан (явно пустое
///        значение — оба потребителя трактуют его как отсутствие ключа);
///        Seed__TeacherPassword задан нестандартный (guard сида пройден —
///        единственная причина отказа должна остаться у ключа подписи).
/// when:  попытка старта приложения (WebApplicationFactory: построение и запуск
///        хоста при создании клиента).
/// then:  старт прерван ошибкой конфигурации; фабрика сообщает ошибку валидации
///        конфигурации (в цепочке исключений — OptionsValidationException с
///        именем Auth__JwtKey); HTTP-запросы не обслуживаются.
/// </summary>
public sealed class Ts050_ProductionWithoutJwtKeyTests
{
    [Fact]
    public void ProductionStartWithoutJwtKey_IsAbortedByConfigurationValidation()
    {
        // given: Production; ключ подписи отсутствует; сид-пароль нестандартный.
        using var factory = new B10ScenarioHostFactory.ProductionNoJwtKey();

        // when: попытка старта хоста (создание клиента строит и запускает хост).
        var startException = Record.Exception(() => factory.CreateClient());

        // then: старт прерван — хост не поднялся, HTTP не обслуживается.
        Assert.True(
            startException is not null,
            "Приложение в Production без Auth__JwtKey стартовало и обслуживает запросы — " +
            "обязательный ключ подписи не проверен (FR-008: отказ старта с ошибкой конфигурации).");

        // then: отказ связан именно с валидацией конфигурации ключа — в цепочке
        // есть OptionsValidationException, чьё сообщение называет Auth__JwtKey.
        // Любой посторонний сбой старта (ошибка DI, регресс Program.cs) такой
        // результат не даёт.
        var keyValidationException = Flatten(startException!)
            .OfType<OptionsValidationException>()
            .FirstOrDefault(exception =>
                exception.Message.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
        Assert.True(
            keyValidationException is not null,
            "Старт прерван, но НЕ ошибкой валидации конфигурации Auth__JwtKey. Исключение: " +
            $"{startException!.GetType().FullName}: {startException.Message}");
    }

    /// <summary>
    /// Развёртка цепочки исключений: сама причина, внутренние исключения и
    /// вложенные AggregateException (хост оборачивает отказы hosted-сервисов).
    /// </summary>
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
