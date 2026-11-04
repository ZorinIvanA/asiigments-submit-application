using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-183 «Seed__DemoData=false: только преподаватель» (FR-025, P1; тип boundary).
///
/// given: Development; Seed__DemoData=false (явно).
/// when:  старт приложения (сид выполняется при построении хоста); инспекция
///        хранилища через DI тестового хоста.
/// then:  создан только преподаватель (teacher существует, студентов нет); списки
///        групп и работ пусты.
/// </summary>
public sealed class Ts183_DevelopmentDemoDataOffTeacherOnlyTests
{
    [Fact]
    public async Task DevelopmentWithDemoDataFalse_SeedsTeacherOnly_EmptyGroupsAndLabs()
    {
        // given: Development-хост; Seed__DemoData=false задано явно
        // (useHarnessDefaults:false — проверяется именно явная конфигурация кейса).
        using var factory = new B02WebAppFactory(
            Environments.Development,
            useHarnessDefaults: false,
            settings: new Dictionary<string, string?>
            {
                ["Seed__DemoData"] = "false",
            });
        _ = factory.CreateWarmClient();

        // when: инспекция хранилища после старта.
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();

        // then: создан только преподаватель; списки групп/работ пусты.
        Assert.NotNull( await users.GetByLoginAsync(SeedOptions.DefaultTeacherLogin) );
        Assert.Empty( await users.ListStudentsAsync() );
        Assert.Empty( await groups.GetAllAsync() );
        Assert.Empty( await labs.GetAllAsync() );
    }
}
