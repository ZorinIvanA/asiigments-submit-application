using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B02.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-139 «Сид: идемпотентность повторного запуска» (FR-025 AC «Идемпотентность»,
/// тип idempotency, P1).
///
/// given: приложение уже запущено один раз с демо-данными; известны текущие количества
///        пользователей/групп/работ/сдач; шов повторного вызова сида доступен в тесте.
/// when:  повторный вызов сида в рамках одного процесса (тестовый шов повторного
///        запуска инициализации — SeedRunner из DI тестового хоста, тот же компонент,
///        что выполняет сид при старте: Program.cs → app.SeedDatabase() → SeedRunner.Run()).
/// then:  числа пользователей/групп/работ/сдач не изменились; дублей по естественным
///        ключам (login/email ci, имя группы ci, (semester, number), (student, lab))
///        нет; uuid могут отличаться (детерминизм не требуется).
/// </summary>
public sealed class Ts139_SeedIdempotencyTests
{
    [Fact]
    public void ReseedingInTheSameProcess_DoesNotChangeCountsOrNaturalKeys()
    {
        // given: Development-хост с демо-данными (Seed__DemoData не задан → умолчание true);
        // первый сид выполнен при построении хоста.
        using var factory = new B02WebAppFactory(Environments.Development, useHarnessDefaults: false);
        _ = factory.CreateWarmClient();

        var users = factory.Services.GetRequiredService<IUserRepository>();
        var groups = factory.Services.GetRequiredService<IGroupRepository>();
        var labs = factory.Services.GetRequiredService<ILabRepository>();
        var submissions = factory.Services.GetRequiredService<ISubmissionRepository>();

        var before = CountAll(users, groups, labs, submissions);
        Assert.True(before.Users > 1, "Хост без сида — кейс недействителен.");

        // when: повторный вызов сида в рамках одного процесса.
        factory.Services.GetRequiredService<SeedRunner>().Run();

        // then: количества не изменились.
        var after = CountAll(users, groups, labs, submissions);
        Assert.Equal(before, after);

        // then: дублей по естественным ключам нет.
        var allUsers = AllUsers(users);
        Assert.Equal(allUsers.Count, allUsers.Select(user => user.Login.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(allUsers.Count, allUsers.Select(user => user.Email.ToLowerInvariant()).Distinct().Count());
        var allGroups = groups.GetAll();
        Assert.Equal(allGroups.Count, allGroups.Select(group => group.Name.ToLowerInvariant()).Distinct().Count());
        var allLabs = labs.GetAll();
        Assert.Equal(allLabs.Count, allLabs.Select(lab => (lab.Semester, lab.Number)).Distinct().Count());
        var allSubmissions = submissions.ListByLabIds(allLabs.Select(lab => lab.Id).ToList());
        Assert.Equal(allSubmissions.Count, allSubmissions.Select(submission => (submission.StudentId, submission.LabId)).Distinct().Count());
    }

    private static IReadOnlyList<User> AllUsers(IUserRepository users)
    {
        var all = new List<User>(users.ListStudents());
        var teacher = users.GetByLogin(SeedOptions.DefaultTeacherLogin);
        if (teacher is not null)
        {
            all.Add(teacher);
        }

        return all;
    }

    private static (int Users, int Groups, int Labs, int Submissions) CountAll(
        IUserRepository users,
        IGroupRepository groups,
        ILabRepository labs,
        ISubmissionRepository submissions) =>
        (AllUsers(users).Count,
            groups.GetAll().Count,
            labs.GetAll().Count,
            submissions.ListByLabIds(labs.GetAll().Select(lab => lab.Id).ToList()).Count);
}
