using LabsApp.Domain.Entities;
using LabsApp.Storage.InMemory;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Storage;

/// <summary>
/// Связка in-memory хранилищ для юнит-тестов C-003: единая <see cref="StorageLock"/>
/// и межрепозиторные каскады (группы→пользователи, работы→сдачи) соединены так же,
/// как в DI-регистрации (StorageServiceCollectionExtensions.AddInMemoryStorage);
/// метки времени репозиториев — общий <see cref="FakeTimeProvider"/> (ADR-002),
/// детерминированно продвигаемый тестом через <see cref="Time"/>.
/// </summary>
internal sealed record TestStorage(
    StorageLock Lock,
    InMemoryUserRepository Users,
    InMemoryGroupRepository Groups,
    InMemoryLabRepository Labs,
    InMemorySubmissionRepository Submissions,
    FakeTimeProvider Time)
{
    public static TestStorage Create()
    {
        var lockObject = new StorageLock();
        var users = new InMemoryUserRepository(lockObject);
        var submissions = new InMemorySubmissionRepository(lockObject);
        var time = new FakeTimeProvider();
        var labs = new InMemoryLabRepository(lockObject, submissions, time);
        var groups = new InMemoryGroupRepository(lockObject, users);
        return new TestStorage(lockObject, users, groups, labs, submissions, time);
    }
}

/// <summary>Строители доменных сущностей для тестов хранилищ.</summary>
internal static class TestEntities
{
    public static User User(
        string login,
        string? email = null,
        string role = UserRoles.Student,
        Guid? groupId = null,
        string? fullName = null) => new()
    {
        Id = Guid.NewGuid(),
        Login = login,
        Email = email ?? $"{login}@example.com",
        PasswordHash = "hash",
        FullName = fullName ?? "Тест Тестович Тестов",
        Role = role,
        GroupId = groupId,
        CreatedAt = DateTime.UtcNow,
    };

    public static Group Group(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTime.UtcNow,
    };

    public static Lab Lab(int semester, int number) => new()
    {
        Id = Guid.NewGuid(),
        Semester = semester,
        Number = number,
        Content = $"Содержание лабораторной работы №{number}",
        DefenseRequired = false,
        AssignmentUrl = null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}
