using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Фикстура лимитерных сценариев волны B-08 (TS-019..TS-022, TS-024;
/// FR-004/FR-007/FR-012): наследует механику <see cref="B08LimitersWebAppFactory"/>
/// (Development, Seed__DemoData=false) и добавляет:
///  - Seed__TeacherPassword = <see cref="SeedOptions.DefaultTeacherPassword"/>
///    ('teacher123!') — given кейса TS-019 «пользователь teacher/teacher123!
///    существует» (в Development умолчание валидно);
///  - бизнес-время — FakeTimeProvider (глоссарий TimeProvider / ADR-002):
///    окно 60 с кейса TS-020 скользит явным SetUtcNow, детерминированно;
///  - IClientIpResolver декорирован <see cref="B08LimitersTestClientIpResolver"/> —
///    транспортный IP клиента задаётся заголовком (given «IP тестового клиента
///    фиксирован», TS-019..TS-021, TS-024).
/// Каждый IClassFixture-класс — свежий экземпляр приложения: лимитеры пусты,
/// счётчик KDF пуст (given TS-019 «счётчик KDF сброшен») — изоляция given кейсов.
/// </summary>
public sealed class B08LimitersRateLimitWebAppFactory : B08LimitersWebAppFactory
{
    /// <summary>
    /// Бизнес-время теста: сдвигается только явными SetUtcNow/Advance сценария;
    /// старт — реальный UtcNow создания фикстуры (механика CR-002).
    /// </summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public B08LimitersRateLimitWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [SeedOptions.TeacherPasswordVariable] = SeedOptions.DefaultTeacherPassword,
            })
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Единый источник бизнес-времени теста: подмена последней регистрацией.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // Транспортный IP клиента — по заголовку TestIpHeader (контракт IF-006).
            services.Replace(ServiceDescriptor.Singleton<IClientIpResolver>(
                _ => new B08LimitersTestClientIpResolver(new ClientIpResolver())));
        });
    }
}
