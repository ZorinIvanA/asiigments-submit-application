using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Связка TestLogSink с тестовым хостом (AC T-004 «Захват записей» и «Изоляция
/// тестов»): sink зарегистрирован в конвейере журналирования хоста, записи любого
/// ILogger доступны из теста по категории/уровню/сообщению. Изоляция — Clear() в
/// конструкторе тестового класса.
/// </summary>
public sealed class TestWebAppFactoryObservabilityTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public TestWebAppFactoryObservabilityTests(TestWebAppFactory factory)
    {
        _factory = factory;
        factory.LogSink.Clear();
    }

    [Fact]
    public void HostLogger_InformationOfArbitraryCategory_VisibleInSink()
    {
        var logger = _factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Custom.Category.FromTest");

        logger.LogInformation("Захвачено {Key} со значением {Value}", "ключ", 42);

        var record = Assert.Single(
            _factory.LogSink.Snapshot(),
            item => item.Category == "Custom.Category.FromTest");
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Захвачено ключ со значением 42", record.Message);
        Assert.Equal("Захвачено {Key} со значением {Value}", record.MessageTemplate);
        Assert.Equal("ключ", record.State["Key"]);
        Assert.Equal(42, record.State["Value"]);
    }

    [Fact]
    public async Task HostPipeline_Warning_ReachesSinkWithCategoryLevelAndMessage()
    {
        // Реальный путь ведения журнала хостом: Seed__DemoData="maybe" даёт
        // предупреждение PostConfigure (категория Hosting.Configuration —
        // общий реестр HostingLogCategories, CR-001: без префикса пространства
        // имён).
        using var localFactory = new TestWebAppFactory(settings:
            new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "maybe" });
        using var client = localFactory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            localFactory.LogSink.Snapshot(),
            record =>
                record.Category == HostingLogCategories.Configuration
                && record.Level == LogLevel.Warning
                && record.Message.Contains(SeedOptions.DemoDataVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void Sink_ClearBetweenTests_NextTestSeesOnlyOwnRecords()
    {
        // Тест A: пишет запись и завершается.
        var loggerA = _factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Isolation.TestA");
        loggerA.LogInformation("запись теста A");
        Assert.Contains(_factory.LogSink.Snapshot(), item => item.Category == "Isolation.TestA");

        // Очистка (в реальном тестовом классе — в конструкторе перед тестом B).
        _factory.LogSink.Clear();

        // Тест B: видит только свои записи.
        var loggerB = _factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Isolation.TestB");
        loggerB.LogWarning("запись теста B");

        var snapshot = _factory.LogSink.Snapshot();
        Assert.DoesNotContain(snapshot, item => item.Category == "Isolation.TestA");
        Assert.Single(snapshot, item => item.Category == "Isolation.TestB");
    }
}
