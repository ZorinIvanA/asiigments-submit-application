using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B14.Infrastructure;
using LabsApp.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// Production-хост кейса TS-173 (given, ревью R4d): окружение Production с ЯВНО
/// заданными Auth__JwtKey (≥32 байт — AuthOptionsValidator) и
/// Seed__TeacherPassword='Str0ng!Unique2026' (SeedOptionsValidator: в Production
/// пароль преподавателя обязателен, ≠ 'teacher123!', правила §8 — иначе fail-fast
/// не даст хосту стартовать); Seed__DemoData=false — из сида создан ТОЛЬКО
/// преподаватель, демо-набор отключён; лабораторных работ хост не создаёт вовсе.
/// Auth__Pbkdf2Iterations=1000 — умолчание тестовых фабрик зоны (скорость старта).
/// Фабрика самодостаточна в файле кейса (чужие файлы инфраструктуры зоны не
/// модифицируются); конфигурация повторяет стенд Production
/// B14ProductionRecoveryWebAppFactory без log-sink — кейсу он не нужен.
/// </summary>
public sealed class Ts173ProductionHostFactory : B14WebAppFactory
{
    /// <summary>Seed__TeacherPassword данного кейса (given TS-173, дословно).</summary>
    public const string ProductionTeacherPassword = "Str0ng!Unique2026";

    /// <summary>Единственный публичный конструктор — для IClassFixture (Production).</summary>
    public Ts173ProductionHostFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Конфигурация зоны B14WebAppFactory повторяется с заменой окружения:
        // base.ConfigureWebHost не вызывается, он фиксирует Development.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(AuthOptions.JwtKeyVariable), TestJwtKey);
        builder.UseSetting(ToConfigKey(SeedOptions.TeacherPasswordVariable), ProductionTeacherPassword);
        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
        builder.UseSetting(ToConfigKey(AuthOptions.Pbkdf2IterationsVariable), "1000");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// TS-173 «semesters: пустой массив при отсутствии работ» (boundary, FR-018, P2,
/// ревью R4d).
///
/// given: Production-конфигурация хоста: заданы Auth__JwtKey и
///        Seed__TeacherPassword='Str0ng!Unique2026' (демо-данные отключены) —
///        создан только преподаватель, лабораторных работ нет; валидная сессия
///        teacher (минт access-JWT, ADR-022).
/// when:  GET /api/v1/semesters
/// then:  200; тело — пустой массив [] (JSON-массив нулевой длины): distinct
///        семестров, по которым есть хотя бы одна лабораторная, при отсутствии
///        работ пуст (FR-018: «массив различных номеров семестров, по которым
///        есть хотя бы одна лабораторная»); НЕ 404, НЕ null, НЕ отсутствие тела.
/// </summary>
public sealed class Ts173_SemestersEmptyProductionTests
    : IClassFixture<Ts173ProductionHostFactory>
{
    private const string SemestersEndpoint = "/api/v1/semesters";

    private readonly Ts173ProductionHostFactory _factory;

    public Ts173_SemestersEmptyProductionTests(Ts173ProductionHostFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Semesters_NoLabs_ReturnsEmptyArrayForTeacher()
    {
        // given: Production-хост: создан только сид-преподаватель (ResolveSeedTeacher
        // утверждает его наличие и роль), лабораторных работ нет (предусловие
        // фиксируется чтением ILabRepository тестового хоста); валидная сессия teacher.
        var teacher = B14Harness.ResolveSeedTeacher(_factory);
        Assert.Empty(_factory.Services.GetRequiredService<ILabRepository>().GetAll());
        using var client = B14Harness.CreateSessionClient(_factory, teacher.Id, UserRoles.Teacher);

        // when: GET /api/v1/semesters
        using var response = await client.GetAsync(SemestersEndpoint);

        // then: 200 — тело JSON-массив НУЛЕВОЙ длины []: не 404, не null, не
        // отсутствие тела (FR-018).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var body = document.RootElement;
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }
}
