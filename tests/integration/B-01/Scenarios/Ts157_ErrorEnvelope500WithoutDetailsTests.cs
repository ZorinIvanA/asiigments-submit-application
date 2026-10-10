using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B01.Infrastructure;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-157 «Конверт ошибок: 500 без деталей исключения» (FR-023, P1).
/// given: в тестовом окружении один сервис — ILabRepository-заглушка — заменён
/// реализацией, бросающей исключение; teacher авторизован.
/// when: вызов затронутого эндпойнта POST /labs — имитация необработанного
/// исключения.
/// then: 500 {'message':'Внутренняя ошибка сервера'}; ответ не содержит имён
/// типов и stack trace (AC FR-023 «500 без деталей»).
/// Примечание: кейс прежней нумерации контура (в текущем списке кейсов батча
/// B-01 не значится) — сохранён в зоне как регресс-покрытие конверта FR-023.
/// </summary>
public sealed class Ts157_ErrorEnvelope500WithoutDetailsTests
{
    [Fact]
    public async Task PostLabs_WhenServiceThrows_ReturnsEnvelopeWithoutExceptionDetails()
    {
        // given: ILabRepository заменён бросающей реализацией (ConfigureTestServices
        // регистрируется позже Program — последняя регистрация побеждает в DI). Хост
        // стартует на штатной реализации (сид выполняется), затем сервис «взводится»
        // — целевой запрос получает замену, бросающую исключение. teacher —
        // валидная access-cookie сеяного учителя (ADR-015: DI-сид + минтованный
        // access-JWT вместо HTTP-входа).
        using var factory = new ArmedRepositoryFactory();
        using var client = MintedSession.CreateTeacherClient(factory);
        ((ArmedLabRepository)factory.Services.GetRequiredService<ILabRepository>()).Arm();

        // when: POST /api/v1/labs с валидным телом — валидация проходит, вызов
        // доходит до заменённого репозитория (_labs.Add) и падает исключением.
        using var response = await client.PostAsJsonAsync(
            "/api/v1/labs",
            new { semester = 1, number = 1, content = "Лабораторная работа" });

        // then: 500; {'message':'Внутренняя ошибка сервера'} — литерал словаря
        // замороженной спеки (FR-023), не прод-константа (иначе дрейф словаря
        // тестом не ловится) — ровно один ключ message в теле (ErrorEnvelopeAsync
        // проверяет множество ключей).
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await JsonAssert.ErrorEnvelopeAsync(response, "Внутренняя ошибка сервера");

        // then: имён типов исключения и stack trace в ответе нет.
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ArmedLabRepository", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Хост с заменённым ILabRepository (given кейса). Замена оборачивает штатную
    /// реализацию (первая регистрация интерфейса): SeedRunner читает репозиторий
    /// при СТАРТЕ хоста, поэтому бросающая замена включается только после старта —
    /// на прикладные запросы.
    /// </summary>
    private sealed class ArmedRepositoryFactory : B01WebAppFactory
    {
        public ArmedRepositoryFactory()
            : base(Environments.Development, new Dictionary<string, string?>
            {
                // Демо-набор не нужен (умолчание Development true) — ускоряем старт.
                ["Seed__DemoData"] = "false",
            })
        {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<ILabRepository>(services =>
                new ArmedLabRepository(
                    // Конкретный класс штатной реализации (InMemoryLabRepository) —
                    // резолв по интерфейсу здесь дал бы рекурсию на собственную
                    // замену (последняя регистрация побеждает).
                    services.GetRequiredService<InMemoryLabRepository>())));
        }
    }

    /// <summary>
    /// Замена сервиса для целевого запроса (FR-024 AC «Подмена реализации»): до
    /// взведения делегирует штатной реализации (старт/сид), после — бросает
    /// исключение на любую операцию.
    /// </summary>
    private sealed class ArmedLabRepository(ILabRepository inner) : ILabRepository
    {
        private volatile bool _armed;

        public void Arm() => _armed = true;

        public void Add(Lab lab)
        {
            ThrowIfArmed();
            inner.Add(lab);
        }

        public Lab? GetById(Guid id)
        {
            ThrowIfArmed();
            return inner.GetById(id);
        }

        public IReadOnlyList<Lab> GetAll()
        {
            ThrowIfArmed();
            return inner.GetAll();
        }

        public IReadOnlyList<Lab> ListByFilter(int? semester)
        {
            ThrowIfArmed();
            return inner.ListByFilter(semester);
        }

        public IReadOnlyList<int> Semesters()
        {
            ThrowIfArmed();
            return inner.Semesters();
        }

        public Lab? TryGetByPair(int semester, int number)
        {
            ThrowIfArmed();
            return inner.TryGetByPair(semester, number);
        }

        public bool ExistsPair(int semester, int number, Guid? exceptId = null)
        {
            ThrowIfArmed();
            return inner.ExistsPair(semester, number, exceptId);
        }

        public void Update(Lab lab)
        {
            ThrowIfArmed();
            inner.Update(lab);
        }

        public void Delete(Guid id)
        {
            ThrowIfArmed();
            inner.Delete(id);
        }

        private void ThrowIfArmed()
        {
            if (_armed)
            {
                throw new Ts157ServiceCrashException();
            }
        }
    }

    /// <summary>Исключение заменённого сервиса: воспроизводит необработанный сбой прикладного кода.</summary>
    private sealed class Ts157ServiceCrashException : InvalidOperationException
    {
        public Ts157ServiceCrashException()
            : base("Целевой запрос обслужен заменённым сервисом (TS-157).")
        {
        }
    }
}
