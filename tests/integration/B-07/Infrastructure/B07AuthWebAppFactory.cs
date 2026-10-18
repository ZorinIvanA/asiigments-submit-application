using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Фикстура auth-сценариев батча B-07 (TS-046..TS-060, FR-012/FR-013/FR-014):
/// наследует механику <see cref="B07WebAppFactory"/> (Development, Seed__DemoData
/// =false — AR-011/NFR-011) и добавляет:
///  - Seed__TeacherPassword='Passw0rd!' — given кейсов «Пользователь teacher с
///    паролем 'Passw0rd!' существует» (TS-049..TS-054; в Development требования
///    §8 к сид-паролю не применяются, FR-006);
///  - бизнес-время — FakeTimeProvider (Microsoft.Extensions.TimeProvider.Testing;
///    TimeProvider — единый источник, глоссарий/ADR-010, CR-002): TTL refresh
///    детерминирован (TS-056, TS-058);
///  - IClientIpResolver декорирован <see cref="B07TestClientIpResolver"/> —
///    транспортный IP клиента задаётся заголовком (given «RemoteIpAddress=
///    10.0.0.x», TS-046..TS-048, TS-053).
/// Каждый IClassFixture-класс — свежий экземпляр приложения: пустые лимитеры,
/// пустое хранилище (кроме сид-преподавателя) — изоляция given кейсов.
/// </summary>
public sealed class B07AuthWebAppFactory : B07WebAppFactory
{
    /// <summary>Сид-пароль преподавателя — пароль кейсов FR-013 (given TS-049).</summary>
    public const string TeacherPassword = "Passw0rd!";

    /// <summary>
    /// Бизнес-время теста: сдвигается только явными вызовами Advance/SetUtcNow
    /// из сценария; старт — реальный UtcNow создания фикстуры (семантика прежней
    /// локальной копии, механизм — пакетный FakeTimeProvider, CR-002).
    /// </summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public B07AuthWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [SeedOptions.TeacherPasswordVariable] = TeacherPassword,
            })
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Единый источник бизнес-времени теста: подмена последней регистрацией —
            // все потребители (JWT, лимитеры, хранилища) получают Time (глоссарий).
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // Транспортный IP клиента — по заголовку TestIpHeader (контракт IF-006).
            services.Replace(ServiceDescriptor.Singleton<IClientIpResolver>(
                _ => new B07TestClientIpResolver(new ClientIpResolver())));
        });
    }
}
