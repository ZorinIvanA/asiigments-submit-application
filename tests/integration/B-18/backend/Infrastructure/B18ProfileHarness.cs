using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B18Profile.Infrastructure;

/// <summary>
/// Предусловия и клиенты тестового хоста зоны B-18/backend (методика волны,
/// ADR-015/CR-001):
///  - пользователи и группы создаются ПРЯМЫМ DI-сидом в IUserRepository/
///    IGroupRepository тестового хоста (POST /auth/login и POST /auth/register
///    в предусловиях НЕ вызываются — auth-эндпоинты удалены);
///  - аутентификации в приложении нет: все эндпоинты публичны, «текущий
///    пользователь» — засеянный преподаватель (Seed__TeacherLogin, умолчание
///    «teacher»); сценарии ходят анонимным клиентом фабрики (cookie/JWT-сессии
///    не существуют).
/// Информация о пользователях для проверок хранилища — IUserRepository из
/// factory.Services (чтение возвращает копии-снимки; контракт IF-015).
/// </summary>
public static class B18ProfileHarness
{
    public const string ProfileEndpoint = "/api/v1/me/profile";

    /// <summary>
    /// Плейсхолдер password_hash DI-сид-пользователей: аутентификации нет, пароль
    /// не проверяется и не хэшируется — колонка NOT NULL (как в SeedRunner).
    /// </summary>
    public const string SeededPasswordHashMark = "!";

    /// <summary>
    /// Клиент сценария: анонимный, без авто-редиректов (точные статусы).
    /// </summary>
    public static HttpClient Create(B18ProfileWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «пользователь создан прямым DI-сидом в IUserRepository тестового хоста;
    /// uuid известен харнесу»: используется как «ДРУГОЙ пользователь» (занятый
    /// email/логин); текущий пользователь сценариев профиля — засеянный
    /// преподаватель.
    /// </summary>
    public static User SeedUser(
        B18ProfileWebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId,
        string? password = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = SeededPasswordHashMark,
            FullName = fullName,
            Role = role,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow,
        };

        factory.Services.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository».</summary>
    public static Group SeedGroup(B18ProfileWebAppFactory factory, string name)
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
    /// given «текущее состояние групп изменилось»: переименование группы прямым
    /// DI-сидом IGroupRepository.Update — ответ профиля должен показать НОВОЕ имя
    /// (вычисление по текущему состоянию групп, FR-015).
    /// </summary>
    public static async Task RenameGroupAsync(B18ProfileWebAppFactory factory, Group group, string newName)
    {
        await factory.Services.GetRequiredService<IGroupRepository>().UpdateAsync(new Group
        {
            Id = group.Id,
            Name = newName,
            CreatedAt = group.CreatedAt,
        });
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static async Task<User> StoredUserAsync(B18ProfileWebAppFactory factory, Guid userId)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>
    /// Текущий пользователь приложения — засеянный преподаватель (Seed__TeacherLogin,
    /// умолчание «teacher»): из хранилища тестового хоста (копия-снимок, IF-015).
    /// </summary>
    public static async Task<User> CurrentUserAsync(B18ProfileWebAppFactory factory)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>()
            .GetByLoginAsync(SeedOptions.DefaultTeacherLogin);
        Assert.NotNull(user);
        return user!;
    }
}
