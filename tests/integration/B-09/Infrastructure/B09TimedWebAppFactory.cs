using System.Globalization;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Хост батча B-09 с инжектируемыми часами (FR-003/ADR-002) — для кейсов
/// TS-039..TS-047 и TS-053..TS-058 (вход/refresh/logout): FakeTimeProvider
/// заменяет TimeProvider, зарегистрированный в Program (коллбэк ConfigureServices
/// выполняется после завершения Program и до старта хоста, поэтому сид и
/// инициализация эталонных хэшей исполняются уже по фиктивному времени —
/// образец B11TimedWebAppFactory). Время движется ТОЛЬКО явно (Advance/SetUtcNow):
/// окно login-лимитера 60 с (FR-004) и TTL refresh 7 суток (FR-008/IF-003)
/// детерминированы.
///
/// Auth__Pbkdf2Iterations=1000 задаётся ДО старта хоста (FR-027: «тесты
/// используют малые Auth__Pbkdf2Iterations»): и сид преподавателя, и эталонный
/// хэш, и все Verify-деривации кейсов исполняются с 1000 итераций.
///
/// Seed__TeacherPassword возвращён к умолчанию спеки 'teacher123!' (пароль
/// сид-преподавателя кейсов TS-039..TS-045: «Пользователь teacher/teacher123!
/// существует (сид)») — базовая фабрика зоны фиксирует собственную константу,
/// кейсы батча используют дословное значение кейса.
/// </summary>
public sealed class B09TimedWebAppFactory : B09WebAppFactory
{
    /// <summary>Малое число итераций KDF тестов (FR-027).</summary>
    public const int TestPbkdf2Iterations = 1000;

    /// <summary>Единственный источник бизнес-времени тестового хоста.</summary>
    public FakeTimeProvider Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(
            ToColonKey(AuthOptions.Pbkdf2IterationsVariable),
            TestPbkdf2Iterations.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            ToColonKey(SeedOptions.TeacherPasswordVariable),
            SeedOptions.DefaultTeacherPassword);

        builder.ConfigureServices(services =>
        {
            // Последняя регистрация TimeProvider выигрывает разрешение из DI:
            // все потребители (движок лимитера, TTL токенов) получают часы теста.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Ключ UseSetting — с разделителем ':' ('__'-иерархию создаёт только провайдер переменных окружения).</summary>
    private static string ToColonKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
