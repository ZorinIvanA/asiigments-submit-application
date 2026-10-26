using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-182 «Идемпотентность сида: повторный запуск не дублирует данные» (FR-025 AC
/// «Идемпотентность»; тип idempotency, P1).
///
/// given: приложение уже запущено один раз (сид развёрнут при построении хоста);
///        известны текущие количества пользователей/групп/работ/сдач.
/// when:  повторный вызов сида в рамках одного процесса (тестовый шов — SeedRunner
///        из DI тестового хоста, тот же компонент, что выполняет сид при старте:
///        Program.cs → app.SeedDatabase() → SeedRunner.Run()); подсчёт записей до
///        и после.
/// then:  число пользователей/групп/работ/сдач не изменилось (идемпотентность по
///        естественным ключам — дублей lower(login)/lower(email)/ci-имя группы/
///        (semester, number)/(student, lab) нет; uuid могут отличаться).
/// </summary>
public sealed class Ts182_SeedIdempotencyTests
{
    [Fact]
    public async Task ReseedingInTheSameProcess_DoesNotChangeCounts()
    {
        // given: Development-хост, запущенный один раз; Seed__DemoData не задан —
        // умолчание Development (true) — сид с демо-набором выполнен при построении.
        using var factory = new B02WebAppFactory(Environments.Development, useHarnessDefaults: false);
        _ = factory.CreateWarmClient();

        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        var before = await CountAllAsync(users, groups, labs, submissions);
        Assert.True(before.Users > 1, "Хост без сида — кейс недействителен.");

        // when: повторный вызов сида в рамках одного процесса (тестовый шов).
        await factory.Services.GetRequiredService<SeedRunner>().RunAsync();

        // then: количества пользователей/групп/работ/сдач не изменились.
        var after = await CountAllAsync(users, groups, labs, submissions);
        Assert.Equal(before, after);

        // then: дублей по естественным ключам нет (идемпотентность FR-025).
        var allUsers = await AllUsersAsync(users);
        Assert.Equal(allUsers.Count, allUsers.Select(user => user.Login.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(allUsers.Count, allUsers.Select(user => user.Email.ToLowerInvariant()).Distinct().Count());
        var allGroups = await groups.GetAllAsync();
        Assert.Equal(allGroups.Count, allGroups.Select(group => group.Name.ToLowerInvariant()).Distinct().Count());
        var allLabs = await labs.GetAllAsync();
        Assert.Equal(allLabs.Count, allLabs.Select(lab => (lab.Semester, lab.Number)).Distinct().Count());
        var allSubmissions = await submissions.ListByLabIdsAsync(allLabs.Select(lab => lab.Id).ToList());
        Assert.Equal(
            allSubmissions.Count,
            allSubmissions.Select(submission => (submission.StudentId, submission.LabId)).Distinct().Count());
    }

    private static async Task<IReadOnlyList<User>> AllUsersAsync(IUserRepository users)
    {
        var all = new List<User>(await users.ListStudentsAsync());
        var teacher = await users.GetByLoginAsync(SeedOptions.DefaultTeacherLogin);
        if (teacher is not null)
        {
            all.Add(teacher);
        }

        return all;
    }

    private static async Task<(int Users, int Groups, int Labs, int Submissions)> CountAllAsync(
        IUserRepository users,
        IGroupRepository groups,
        ILabRepository labs,
        ISubmissionRepository submissions)
    {
        var allLabs = await labs.GetAllAsync();
        return ((await AllUsersAsync(users)).Count,
            (await groups.GetAllAsync()).Count,
            allLabs.Count,
            (await submissions.ListByLabIdsAsync(allLabs.Select(lab => lab.Id).ToList())).Count);
    }
}
