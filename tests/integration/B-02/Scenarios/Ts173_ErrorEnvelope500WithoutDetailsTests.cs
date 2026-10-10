using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-173 «Конверт ошибок: 500 без деталей исключения» (FR-023 AC «500 без деталей»,
/// P0; тип negative).
///
/// given: в тестовом окружении один из прикладных сервисов — ILabRepository —
///        заменён реализацией, бросающей исключение (тестовый шов подмены через DI:
///        ConfigureTestServices регистрируется позже Program — последняя регистрация
///        интерфейса побеждает); teacher авторизован (Development-хост, штатный вход
///        сеяного преподавателя).
/// when:  запрос, проходящий через аварийный сервис — POST /api/v1/labs с валидным
///        телом: полевая валидация проходит, вызов доходит до заменённого
///        репозитория (_labs.Add) и падает необработанным исключением.
/// then:  500 {'message':'Внутренняя ошибка сервера'} — литерал словаря замороженной
///        спеки (не прод-константа: дрейф словаря тестом ловится); ответ не содержит
///        имён типов исключения и stack trace (FR-023 AC «500 без деталей»).
/// </summary>
public sealed class Ts173_ErrorEnvelope500WithoutDetailsTests
{
    [Fact]
    public async Task PostLabs_ThroughThrowingService_ReturnsEnvelopeWithoutExceptionDetails()
    {
        // given: хост с заменённым ILabRepository; teacher — валидная сессия
        // (Development, дефолтный сид-пароль допустим). Замена оборачивает штатную
        // реализацию: сид при СТАРТЕ хоста идёт через делегирование, бросающий
        // режим «взводится» после входа — только на целевой запрос.
        // B02WebAppFactory запечатан — подмена сервиса через WithWebHostBuilder
        // (ConfigureTestServices применяется после ConfigureWebHost фабрики —
        // последняя регистрация интерфейса побеждает в DI).
        using var baseFactory = new B02WebAppFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILabRepository>(services =>
                new ArmedLabRepository(services.GetRequiredService<InMemoryLabRepository>()))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using var login = await HostClients.LoginAsDefaultTeacherAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        ((ArmedLabRepository)factory.Services.GetRequiredService<ILabRepository>()).Arm();

        // when: POST /api/v1/labs с валидным телом — исключение заменённого сервиса.
        using var response = await client.PostAsJsonAsync(
            "/api/v1/labs",
            new { semester = 1, number = 1, content = "Лабораторная работа (TS-173)" });

        // then: 500 с конвертом {'message':'Внутренняя ошибка сервера'}.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Внутренняя ошибка сервера", payload.GetProperty("message").GetString());

        // then: имён типов исключения и stack trace в ответе нет.
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ArmedLabRepository", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Замена сервиса (FR-024 AC «Подмена реализации»): до взведения делегирует
    /// штатной реализации (старт/сид), после — бросает исключение на любую операцию.
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
                throw new Ts173ServiceCrashException();
            }
        }
    }

    /// <summary>Исключение заменённого сервиса: имитация необработанного сбоя прикладного кода.</summary>
    private sealed class Ts173ServiceCrashException : InvalidOperationException
    {
        public Ts173ServiceCrashException()
            : base("Целевой запрос обслужен заменённым сервисом (TS-173).")
        {
        }
    }
}
