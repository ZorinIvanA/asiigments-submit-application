using System.Net;
using LabsApp.Observability;
using LabsApp.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Связка C-012 с композиция-корнем (IF-005/IF-016, Program.cs): в Development
/// резолвится DevEmailSender, в Production — ProductionEmailSender;
/// ISecurityEventLogger доступен в обоих окружениях; реальный конвейер даёт
/// ровно одну запись Api.Request на запрос (NFR-006). Заглушка email на хостовом
/// log-sink: Development — запись «EmailDev» с маркером [DEV-EMAIL], среды
/// ≠ Development — один warning без адресата и содержимого.
/// </summary>
public sealed class ObservabilityCompositionTests
{
    [Fact]
    public void Development_ResolvesDevEmailSenderAndSecurityEventLogger()
    {
        using var factory = new TestWebAppFactory();
        _ = factory.Services;

        Assert.IsType<DevEmailSender>(factory.Services.GetRequiredService<IEmailSender>());
        Assert.IsType<SecurityEventLogger>(factory.Services.GetRequiredService<ISecurityEventLogger>());
    }

    [Fact]
    public void Production_ResolvesProductionEmailSenderAndSecurityEventLogger()
    {
        using var factory = new TestWebAppFactory(Environments.Production);
        _ = factory.Services;

        Assert.IsType<ProductionEmailSender>(factory.Services.GetRequiredService<IEmailSender>());
        Assert.IsType<SecurityEventLogger>(factory.Services.GetRequiredService<ISecurityEventLogger>());
    }

    [Fact]
    public async Task HostEmailSender_Development_WritesSingleEmailDevRecordToHostSink()
    {
        // AC T-103 «Dev-заглушка» на хостовой композиции: реализация выбрана по
        // IHostEnvironment, письмо уходит в host-log-sink категорией «EmailDev».
        using var factory = new TestWebAppFactory();
        _ = factory.Services;
        factory.LogSink.Clear();

        await factory.Services.GetRequiredService<IEmailSender>()
            .SendAsync("a@b.ru", "Код восстановления пароля", "Код: 042713");

        var record = Assert.Single(
            factory.LogSink.Snapshot(),
            item => item.Category == DevEmailSender.LogCategory);
        Assert.Equal(LogLevel.Information, record.Level);
        var serialized = $"{record.Message}|{record.State["To"]}|{record.State["Body"]}";
        Assert.Contains(DevEmailSender.DevEmailMarker, serialized, StringComparison.Ordinal);
        Assert.Contains("a@b.ru", serialized, StringComparison.Ordinal);
        Assert.Contains("042713", serialized, StringComparison.Ordinal);

        // Единственность канала: вне «EmailDev» кода нет (NFR-006).
        Assert.DoesNotContain(
            factory.LogSink.Snapshot(),
            item => item.Category != DevEmailSender.LogCategory
                && $"{item.Message}|{item.MessageTemplate}".Contains("042713", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostEmailSender_Production_WritesSingleWarningWithoutContent()
    {
        // AC T-103 «No-op вне dev» на хостовой композиции: Production-хост
        // резолвит no-op реализацию — один warning без адресата и содержимого.
        using var factory = new TestWebAppFactory(Environments.Production);
        _ = factory.Services;
        factory.LogSink.Clear();

        await factory.Services.GetRequiredService<IEmailSender>()
            .SendAsync("a@b.ru", "Код восстановления пароля", "Код: 042713");

        var record = Assert.Single(
            factory.LogSink.Snapshot(),
            item => item.Category == ProductionEmailSender.LogCategory);
        Assert.Equal(LogLevel.Warning, record.Level);
        var serialized = string.Concat(
            record.Category, "|", record.Message, "|", record.MessageTemplate);
        Assert.DoesNotContain("a@b.ru", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("042713", serialized, StringComparison.Ordinal);

        // Письмо не пишется: записей «EmailDev» вне Development нет вовсе.
        Assert.DoesNotContain(
            factory.LogSink.Snapshot(),
            item => item.Category == DevEmailSender.LogCategory);
    }

    [Fact]
    public async Task HostPipeline_SingleRequest_ExactlyOneRequestLogRecord()
    {
        // NFR-006 на реальном конвейере: один запрос → ровно одна запись
        // Api.Request с обязательными полями. Путь без маршрута — route-404
        // (анонимный, без завязки на сценарии аутентификации доменных волн).
        using var factory = new TestWebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/__no_route__");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var record = Assert.Single(
            factory.LogSink.Snapshot(),
            item => item.Category == ObservabilityMiddleware.RequestLogCategory);
        Assert.Equal("GET", record.State["Method"]);
        Assert.Equal("/api/v1/__no_route__", record.State["Path"]);
        Assert.Equal(404, (int)record.State["Status"]!);
        Assert.True((double)record.State["DurationMs"]! >= 0);
    }
}
