using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-055 «Development без Auth__JwtKey — эфемерный ключ и предупреждение»
/// (boundary, FR-008, P2).
///
/// given: Environment=Development; Auth__JwtKey не задан (явно пустое значение);
///        тестовый log-sink подключён к хосту.
/// when:  старт приложения; вход teacher/teacher123!.
/// then:  приложение стартует; в логе есть предупреждение о незаданном ключе;
///        вход — 200 (токены подписаны эпизодическим случайным ключом; FR-008:
///        «в Development — эпизодический случайный ключ и предупреждение в лог»).
/// </summary>
public sealed class B10Ts055_DevelopmentEphemeralKeyTests
{
    [Fact]
    public async Task DevelopmentWithoutJwtKey_StartsServesLoginAndWarnsAboutMissingKey()
    {
        // given: Development; Auth__JwtKey не задан; log-sink подключён к хосту;
        // клиент без cookie-контейнера.
        using var factory = new B10ScenarioHostFactory.DevelopmentNoJwtKey();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // when: старт приложения и вход teacher/teacher123!.
        using var response = await client.PostAsJsonAsync(
            B10CookieFlow.LoginPath,
            new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = SeedOptions.DefaultTeacherPassword,
            });

        // then: приложение стартует и обслуживает вход — 200 (токены подписаны
        // эпизодическим случайным ключом — иначе старт был бы прерван).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Приложение в Development без Auth__JwtKey обязано стартовать и обслуживать " +
            $"вход teacher/teacher123! (200), фактически {(int)response.StatusCode} " +
            $"{response.StatusCode}, тело: {await response.Content.ReadAsStringAsync()}.");

        // then: в логе есть предупреждение именно о незаданном ключе: категория
        // конфигурации хостинга И текст, называющий Auth__JwtKey либо ключ
        // подписи (конъюнкция — нерелевантное Warning не засчитывается).
        var keyWarning = factory.LogSink.Snapshot().Any(entry =>
            entry.Level == LogLevel.Warning
            && entry.Category.Contains("Hosting.Configuration", StringComparison.Ordinal)
            && (entry.Message.Contains("JwtKey", StringComparison.Ordinal)
                || entry.Message.Contains("ключ подписи", StringComparison.OrdinalIgnoreCase)));
        Assert.True(
            keyWarning,
            "В логе нет предупреждения о незаданном Auth__JwtKey. Захваченные Warning-записи: "
            + DescribeWarnings(factory.LogSink.Snapshot()));
    }

    /// <summary>Текстовая сводка Warning-записей — диагностика сообщения об отказе.</summary>
    private static string DescribeWarnings(IReadOnlyList<B10LogEntry> entries)
    {
        var warnings = entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => $"[{entry.Category}] {entry.Message}")
            .ToList();
        return warnings.Count == 0 ? "нет ни одной Warning-записи" : string.Join("; ", warnings);
    }
}
