using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B02.Infrastructure;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-136 «Сид: Production с незаданным или дефолтным Seed__TeacherPassword
/// не стартует» (FR-025 AC «Production с дефолтным сид-паролем не стартует» /
/// SEC-001, CWE-798, P1; тип negative).
///
/// given: ASPNETCORE_ENVIRONMENT=Production; Auth__JwtKey задан; ДВА прогона:
///        (а) Seed__TeacherPassword не задан, (б) Seed__TeacherPassword='teacher123!'.
/// when:  попытка старта приложения в обоих прогонах.
/// then:  оба старта прерваны ошибкой конфигурации (ненулевой код выхода процесса,
///        в выводе — диагностическое сообщение guard'а с именем переменной
///        Seed__TeacherPassword — отличие от произвольного сбоя сборки/старта);
///        HTTP-запросы не обслуживаются (порт закрыт).
///
/// Проверка процессом dotnet run: guard конфигурации — fail-fast при построении хоста,
/// наблюдается честно на уровне процесса (код выхода + состояние порта).
/// </summary>
[Collection(B02SerialProcessCollection.Name)]
public sealed class Ts136_ProductionSeedPasswordGuardTests
{
    /// <summary>Валидный Production-ключ подписи JWT (64 символа, не dev-ключ).</summary>
    private const string ProductionJwtKey =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(180);

    /// <summary>Прогон (а): Seed__TeacherPassword не задан.</summary>
    [Fact]
    public Task Production_WithoutTeacherPassword_FailsStartupAndDoesNotServe() =>
        StartAndAssertConfigurationFailureAsync(teacherPassword: null);

    /// <summary>Прогон (б): Seed__TeacherPassword равен документированному дефолту.</summary>
    [Fact]
    public Task Production_WithDefaultTeacherPassword_FailsStartupAndDoesNotServe() =>
        StartAndAssertConfigurationFailureAsync("teacher123!");

    private static async Task StartAndAssertConfigurationFailureAsync(string? teacherPassword)
    {
        var port = Ports.GetFreeTcpPort();
        using var app = AppProcess.Start("Production", port, new Dictionary<string, string?>
        {
            ["Auth__JwtKey"] = ProductionJwtKey,
            // null — переменная гарантированно удаляется из окружения дочернего процесса.
            ["Seed__TeacherPassword"] = teacherPassword,
        });

        // when/then: старт прерван ошибкой конфигурации — процесс завершился сам
        // (CR-004: при тайм-ауте в сообщении приводится накопленный вывод процесса).
        var exited = await app.WaitForExitAsync(StartupTimeout);
        Assert.True(
            exited,
            "Процесс приложения не завершился за тайм-аут — старт не был прерван guard'ом. Вывод процесса:"
            + Environment.NewLine + app.Output);
        Assert.NotEqual(0, app.ExitCode);

        // then: причина отказа — именно guard конфигурации, а не произвольный сбой
        // сборки/старта (те тоже дают ненулевой код выхода и закрытый порт):
        // накопленный вывод содержит диагностическое сообщение валидатора
        // (SeedOptionsValidator) с именем переменной Seed__TeacherPassword.
        Assert.True(
            app.Output.Contains(SeedOptions.TeacherPasswordVariable, StringComparison.Ordinal),
            "Процесс завершился с ненулевым кодом выхода без диагностического сообщения "
            + $"guard'а («{SeedOptions.TeacherPasswordVariable}» в выводе нет) — отказ мог быть "
            + "вызван не конфигурацией сида. Вывод процесса:"
            + Environment.NewLine + app.Output);

        // then: HTTP-запросы не обслуживаются — соединение с /health отклоняется
        // (CR-001: отказ соединения даёт не-null; любой ответ сервера — null и падение).
        var failure = await HttpProbes.ProbeHealthFailureAsync(port);
        Assert.True(
            failure is not null,
            $"Порт {port} обслуживает HTTP-запросы — ожидалось, что после прерывания старта он не прослушивается.");
    }
}
