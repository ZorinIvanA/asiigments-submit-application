using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// DI-сид пользователей кейсов регистрации B-03 (ADR-015): прямое наполнение
/// IUserRepository из factory.Services, минуя публичный API (данные given «существует
/// пользователь…» — TS-034/TS-035; обход регистрационного лимита 5/час на IP, FR-004(б)).
/// Пароли сид-записей кейсами не используются (регистрация и дубликаты не проверяют
/// пароли существующих пользователей), поэтому кладётся не-хэшевая заглушка.
/// </summary>
public static class B03UserSeed
{
    /// <summary>
    /// Студент с заданными логином и email: добавляется в IUserRepository, если логин
    /// свободен; иначе возвращается существующая запись (идемпотентность сида —
    /// тестовый класс инстанцируется xUnit'ом на каждый тест-метод).
    /// </summary>
    public static User AddStudent(WebApplicationFactory<Program> factory, string login, string email)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        if (users.GetByLogin(login) is { } existing)
        {
            return existing;
        }

        var student = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            FullName = $"Сид-студент {login}",
            Role = UserRoles.Student,
            GroupId = null,
            PasswordHash = "di-seed-hash-not-used-by-register-cases",
            CreatedAt = DateTime.UtcNow,
        };
        users.Add(student);
        return student;
    }

    /// <summary>Число студентов в хранилище (шаг given «известен состав пользователей» и проверка «пользователь не создан»).</summary>
    public static int CountStudents(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IUserRepository>().ListStudents().Count;

    /// <summary>Пользователь по логину без учёта регистра либо null (проверка «пользователь не создан»).</summary>
    public static User? FindByLogin(WebApplicationFactory<Program> factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login);
}
