using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Тестовый хост кейсов восстановления пароля и сброса пароля батча B-14
/// (TS-073..TS-085, TS-207). Наследник фабрики зоны B14WebAppFactory (окружение
/// Development, Auth__JwtKey, Seed__TeacherPassword, Seed__DemoData=false —
/// общая методика зоны; файл B14WebAppFactory.cs не модифицируется — чужие файлы
/// тестов не правятся). Добавки под кейсы восстановления:
///  - B14ControllableClock вместо TimeProvider.System (FR-003/ADR-002: инжектируемые
///    часы; TS-077/TS-081 переводят время за expiresAt, TS-073 — точно на границу
///    TTL reset-токена 15 минут); ConfigureServices выполняется ПОСЛЕ регистраций
///    Program, поэтому RemoveAll+AddSingleton подменяет TimeProvider.System;
///  - B14RecoveryLogSink в конвейере журналирования — given кейсов TS-073/TS-207:
///    живой код извлекается из [DEV-EMAIL]-записи (IF-005/ADR-012);
///  - Auth__Pbkdf2Iterations=1000 (tech solution: умолчание тестовых фабрик;
///    текущий Pbkdf2PasswordHasher ключ пока игнорирует — задел под реворк IF-002).
/// Изоляция сценариев: каждый тестовый КЛАСС со своей IClassFixture-фикстурой
/// получает свежий хост — пустые хранилища и лимитеры; «живой код» добывается
/// собственным POST /auth/recovery/request (лимит recovery_request 3/час
/// в пределах кейса не задействуется повторно).
/// </summary>
public sealed class B14RecoveryWebAppFactory : B14WebAppFactory
{
    /// <summary>Log-sink хоста: извлечение [DEV-EMAIL]-записей (IF-005).</summary>
    public B14RecoveryLogSink LogSink { get; } = new();

    /// <summary>Инжектируемые часы хоста (FR-003): Advance — перевод времени.</summary>
    public B14ControllableClock Clock { get; } = new();

    /// <summary>Единственный публичный конструктор — для IClassFixture (Development).</summary>
    public B14RecoveryWebAppFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Базовая конфигурация зоны (Development, ключи, сид) — затем добавки кейсов.
        base.ConfigureWebHost(builder);

        // Реворк IF-002: итерации PBKDF2 из конфигурации (тесты — 1000); на текущей
        // реализации ключ не читается и потому безвреден.
        builder.UseSetting(ToConfigKey("Auth__Pbkdf2Iterations"), "1000");

        builder.ConfigureLogging(logging => logging.AddProvider(LogSink));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}
