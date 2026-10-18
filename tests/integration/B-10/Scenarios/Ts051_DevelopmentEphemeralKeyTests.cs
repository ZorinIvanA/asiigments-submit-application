using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-051 «Development без Auth__JwtKey — эпизодический ключ и предупреждение»
/// (boundary, FR-008, P2).
///
/// given: ASPNETCORE_ENVIRONMENT=Development; Auth__JwtKey не задан; тестовый
///        log-sink подключён.
/// when:  старт приложения и успешный вход (учётные данные сида).
/// then:  приложение стартует и обслуживает вход (200); в логе присутствует
///        предупреждение о пустом ключе (FR-008: «в Development — эпизодический
///        случайный ключ и предупреждение в лог»).
/// </summary>
public sealed class Ts051_DevelopmentEphemeralKeyTests
{
    [Fact]
    public async Task DevelopmentWithoutJwtKey_StartsServesLoginAndWarnsAboutMissingKey()
    {
        // given: Development; Auth__JwtKey не задан (явно пустое значение);
        // log-sink подключён к хосту; клиент без cookie-контейнера.
        using var factory = new B10ScenarioHostFactory.DevelopmentNoJwtKey();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // when: старт приложения и успешный вход сеяным учителем.
        using var response = await client.PostAsJsonAsync(
            B10CookieFlow.LoginPath,
            new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = SeedOptions.DefaultTeacherPassword,
            });

        // then: приложение стартовало и обслуживает вход — 200.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Приложение в Development без Auth__JwtKey обязано стартовать и обслуживать " +
            $"вход (200), фактически {(int)response.StatusCode} {response.StatusCode}, тело: " +
            $"{await response.Content.ReadAsStringAsync()}.");

        // then: в логе присутствует предупреждение именно о пустом ключе подписи:
        // категория конфигурации хостинга И текст, называющий Auth__JwtKey либо
        // ключ подписи (конъюнкция — нерелевантное Warning не засчитывается).
        var keyWarning = factory.LogSink.Snapshot().Any(entry =>
            entry.Level == LogLevel.Warning
            && entry.Category.Contains("Hosting.Configuration", StringComparison.Ordinal)
            && (entry.Message.Contains("JwtKey", StringComparison.Ordinal)
                || entry.Message.Contains("ключ подписи", StringComparison.OrdinalIgnoreCase)));
        Assert.True(
            keyWarning,
            "В логе нет предупреждения о пустом Auth__JwtKey. Захваченные Warning-записи: "
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
