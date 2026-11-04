using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Предусловия и клиенты тестового хоста батча B-15 (арбитраж a-017/CR-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/
///    IGroupRepository тестового хоста (методика NFR-001/FR-007/ADR-010);
///  - аутентификации в приложении нет: все эндпоинты публичны, «текущий
///    пользователь» — засеянный преподаватель (Seed__TeacherLogin, умолчание
///    «teacher»); сценарии ходят обычным анонимным клиентом фабрики
///    (cookie/JWT-сессии не существуют).
/// Информация о пользователях для проверок хранилища — IUserRepository из
/// factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B15Harness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";

    /// <summary>Перечень семестров (FR-018): GET /api/v1/semesters, доступ анонимный.</summary>
    public const string SemestersEndpoint = "/api/v1/semesters";

    /// <summary>
    /// Плейсхолдер password_hash DI-сид-пользователей: аутентификации нет, пароль
    /// не проверяется и не хэшируется — колонка NOT NULL (как в SeedRunner).
    /// </summary>
    public const string SeededPasswordHashMark = "!";

    /// <summary>
    /// given «клиент сценария»: обычный анонимный клиент фабрики (без cookie и
    /// заголовков авторизации — приложение работает без аутентификации).
    /// </summary>
    public static HttpClient CreateClient(B15WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    /// <summary>
    /// given «пользователь role=student с текущими fullName/email создан прямым
    /// DI-сидом в IUserRepository тестового хоста; uuid известен харнесу».
    /// Используется как «ДРУГОЙ пользователь» (занятый email/логин) в кейсах
    /// профиля; текущий пользователь сценариев — засеянный преподаватель.
    /// </summary>
    public static User SeedStudent(
        B15WebAppFactory factory,
        string fullName,
        string login,
        string email,
        Guid? groupId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = SeededPasswordHashMark,
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository» (кейс TS-096).</summary>
    public static Group SeedGroup(B15WebAppFactory factory, string name)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IGroupRepository>().AddAsync(group);
        return group;
    }

    /// <summary>
    /// given «лабораторная работа создана DI-сидом ILabRepository» (кейсы semesters):
    /// запись Lab добавляется напрямую в ILabRepository тестового хоста — семестр/
    /// номер передаёт вызывающий, остальное — нейтральные значения.
    /// </summary>
    public static Lab SeedLab(B15WebAppFactory factory, int semester, int number)
    {
        var now = DateTime.UtcNow;
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = semester,
            Number = number,
            Content = $"Лабораторная {semester}:{number} (сид B-15)",
            AssignmentUrl = null,
            DefenseRequired = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        factory.Services.GetRequiredService<ILabRepository>().AddAsync(lab);
        return lab;
    }

    /// <summary>
    /// Текущий пользователь приложения — засеянный преподаватель (Seed__TeacherLogin,
    /// умолчание «teacher»): из хранилища тестового хоста (копия-снимок, IF-015).
    /// </summary>
    public static async Task<User> CurrentUserAsync(B15WebAppFactory factory)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>()
            .GetByLoginAsync(SeedOptions.DefaultTeacherLogin);
        Assert.NotNull(user);
        return user;
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static async Task<User> UserByIdAsync(B15WebAppFactory factory, Guid id)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>().GetByIdAsync(id);
        Assert.NotNull(user);
        return user;
    }

    /// <summary>Пользователь по email (ci) из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static async Task<User> UserByEmailAsync(B15WebAppFactory factory, string email)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>().GetByEmailAsync(email);
        Assert.NotNull(user);
        return user;
    }
}
