using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Хост батча B-11 с инжектируемыми часами (FR-003/ADR-002): FakeTimeProvider
/// заменяет TimeProvider, зарегистрированный в Program (коллбэк ConfigureServices
/// выполняется после завершения Program и до старта хоста, поэтому сид и
/// инициализация эталонных хэшей исполняются уже по фиктивному времени).
///
/// Время движется ТОЛЬКО явно (Advance/SetUtcNow): окно login-лимитера 60 с
/// (FR-004) и TTL refresh 7 суток (FR-008/IF-003) детерминированы — кейсы
/// TS-040/TS-042/TS-046 (скольжение окна) и TS-053 (просроченный refresh)
/// не зависят от настенных часов машины.
/// </summary>
public sealed class B11TimedWebAppFactory : B11WebAppFactory
{
    /// <summary>Единственный источник бизнес-времени тестового хоста.</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Последняя регистрация TimeProvider выигрывает разрешение из DI:
            // все потребители (движок лимитера, TTL токенов) получают часы теста.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}
