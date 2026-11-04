using System.Text.Json;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B14.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B14.Scenarios;

/// <summary>
/// Production-хост кейса TS-173 (given, ревью R4d): окружение Production;
/// Seed__DemoData=false — из сида создан ТОЛЬКО преподаватель, демо-набор
/// отключён; лабораторных работ хост не создаёт вовсе. Фабрика самодостаточна
/// в файле кейса (чужие файлы инфраструктуры зоны не модифицируются).
/// Аутентификации в приложении нет — Auth__*-секреты и пароль сид-учётки
/// (Seed__TeacherPassword) упразднены, валидаторы хосту не требуют их.
/// </summary>
public sealed class Ts173ProductionHostFactory : B14WebAppFactory
{
    /// <summary>Единственный публичный конструктор — для IClassFixture (Production).</summary>
    public Ts173ProductionHostFactory()
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Хранилище приложения — PostgreSQL-only; тесты зоны работают на in-memory
        // двойках tests/TestInfrastructure (без ввода-вывода и внешней БД).
        builder.ConfigureServices(s => LabsApp.TestInfrastructure.InMemoryTestStorage.Register(s));
        // Явная строка подключения (не равна dev-умолчанию): Production-режим
        // тестовых хостов проходит StorageOptionsValidator; двойки хранилища
        // подключений не выполняют.
        builder.UseSetting("Storage:ConnectionString", "Host=127.0.0.1;Port=5434;Database=labsapp-test-double");
        // Конфигурация зоны B14WebAppFactory повторяется с заменой окружения:
        // base.ConfigureWebHost не вызывается, он фиксирует Development.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting(WebHostDefaults.ContentRootKey, AppContext.BaseDirectory);

        builder.UseSetting(ToConfigKey(SeedOptions.DemoDataVariable), "false");
    }

    private static string ToConfigKey(string environmentVariableName) =>
        environmentVariableName.Replace("__", ":");
}

/// <summary>
/// TS-173 «semesters: пустой массив при отсутствии работ» (boundary, FR-018, P2,
/// ревью R4d).
///
/// given: Production-конфигурация хоста: демо-данные отключены — создан только
///        преподаватель, лабораторных работ нет; анонимный клиент (аутентификации
///        нет, эндпойнты публичны).
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
    public async Task Semesters_NoLabs_ReturnsEmptyArray()
    {
        // given: Production-хост: создан только сид-преподаватель (ResolveSeedTeacher
        // утверждает его наличие и роль), лабораторных работ нет (предусловие
        // фиксируется чтением ILabRepository тестового хоста); анонимный клиент.
        await B14Harness.ResolveSeedTeacherAsync(_factory);
        Assert.Empty(await _factory.Services.GetRequiredService<ILabRepository>().GetAllAsync());
        using var client = B14Harness.Create(_factory);

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
