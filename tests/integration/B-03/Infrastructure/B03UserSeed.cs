using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;
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
    public static async Task<User> AddStudentAsync(WebApplicationFactory<Program> factory, string login, string email)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        if (await users.GetByLoginAsync(login) is { } existing)
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
        await users.AddAsync(student);
        return student;
    }

    /// <summary>Число студентов в хранилище (шаг given «известен состав пользователей» и проверка «пользователь не создан»).</summary>
    public static async Task<int> CountStudentsAsync(WebApplicationFactory<Program> factory) =>
        (await factory.Services.GetRequiredService<IUserRepository>().ListStudentsAsync()).Count;

    /// <summary>Пользователь по логину без учёта регистра либо null (проверка «пользователь не создан»).</summary>
    public static async Task<User?> FindByLoginAsync(WebApplicationFactory<Program> factory, string login) =>
        await factory.Services.GetRequiredService<IUserRepository>().GetByLoginAsync(login);
}
