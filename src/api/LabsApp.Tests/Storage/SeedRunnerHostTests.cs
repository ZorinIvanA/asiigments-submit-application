using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Tests.Hosting;
using LabsApp.Tests.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Проверки сида через построенный хост (Program.cs: AddInMemoryStorage + SeedDatabase):
/// состав демо-набора в Development по умолчанию (AC FR-004) и предупреждение
/// в журнале при некорректном Seed__DemoData с именем переменной и значением.
/// HTTP-запросов нет: сид выполняется при построении приложения, состояние
/// читается напрямую из DI и log-sink (ADR-010/ADR-018).
/// </summary>
public sealed class SeedRunnerHostTests
{
    [Fact]
    public void Development_ByDefault_DemoSetSeededAtHostBuild()
    {
        // Умолчания харнеса отключены (T-006): тест проверяет умолчание ПРИЛОЖЕНИЯ
        // (Development без явной переменной → демо-набор), а не умолчание фабрики.
        using var factory = new TestWebAppFactory(infrastructureDefaults: false);
        _ = factory.Services; // построение приложения: AddInMemoryStorage + SeedDatabase

        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        Assert.NotNull(users.GetByLogin("teacher"));
        Assert.Equal(32, users.ListStudents().Count);
        Assert.Equal(3, groups.GetAll().Count);
        Assert.Equal(23, labs.GetAll().Count);
        var allLabIds = labs.GetAll().Select(lab => lab.Id).ToList();
        Assert.Equal(4, submissions.ListByLabIds(allLabIds).Count);
    }

    [Fact]
    public void Production_ByDefault_TeacherOnly()
    {
        using var factory = new TestWebAppFactory(Environments.Production);
        _ = factory.Services;

        var users = factory.Services.GetRequiredService<IUserRepository>();

        Assert.NotNull(users.GetByLogin("teacher"));
        Assert.Empty(users.ListStudents());
    }

    // AC FR-007 v2.3 «Демо-данные не создаются в Production»: явное Seed__DemoData=true
    // игнорируется — opt-in в Production удалён.
    [Fact]
    public void Production_ExplicitDemoDataTrue_TeacherOnly()
    {
        using var factory = new TestWebAppFactory(
            Environments.Production,
            new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });
        _ = factory.Services;

        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();

        Assert.NotNull(users.GetByLogin("teacher"));
        Assert.Empty(users.ListStudents());
        Assert.Empty(groups.GetAll());
        Assert.Empty(labs.GetAll());
    }

    // AC FR-004 «Некорректное Seed__DemoData»: применено умолчание окружения
    // (Development → демо-набор), предупреждение содержит имя переменной и значение.
    [Fact]
    public void SeedDemoData_UnknownValue_WarnsAndAppliesEnvironmentDefault()
    {
        using var factory = new TestWebAppFactory(
            settings: new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "yes" });
        _ = factory.Services;

        var warning = factory.LogSink.Snapshot().FirstOrDefault(record =>
            record.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && record.Message.Contains(SeedOptions.DemoDataVariable, StringComparison.Ordinal));

        Assert.NotNull(warning);
        Assert.Contains("yes", warning!.Message, StringComparison.Ordinal);

        var users = factory.Services.GetRequiredService<IUserRepository>();
        Assert.Equal(32, users.ListStudents().Count); // умолчание Development применено
    }

    [Fact]
    public void SeedDemoData_ExplicitFalse_NoWarning_DemoAbsent()
    {
        using var factory = new TestWebAppFactory(
            settings: new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "false" });
        _ = factory.Services;

        var warnings = factory.LogSink.Snapshot().Where(record =>
            record.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && record.Message.Contains(SeedOptions.DemoDataVariable, StringComparison.Ordinal));

        Assert.Empty(warnings);
        var users = factory.Services.GetRequiredService<IUserRepository>();
        Assert.Empty(users.ListStudents());
    }
}
