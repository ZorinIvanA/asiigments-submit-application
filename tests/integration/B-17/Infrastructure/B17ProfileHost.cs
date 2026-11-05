using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Предусловия и клиенты доменной зоны профиля батча B-17 (кейсы TS-081, TS-082,
/// TS-091..TS-096, TS-170, TS-176 — FR-015; методика волны, ADR-015/CR-001; файл
/// зоны — собственная копия механики харнеса B-16, чужие зоны недоступны для
/// ссылок — BL-001):
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
public static class B17ProfileHost
{
    /// <summary>Эндпоинт кейсов зоны профиля (FR-015).</summary>
    public const string ProfileEndpoint = "/api/v1/me/profile";

    /// <summary>
    /// Плейсхолдер password_hash DI-сид-пользователей: аутентификации нет, пароль
    /// не проверяется и не хэшируется — колонка NOT NULL (как в SeedRunner).
    /// </summary>
    public const string SeededPasswordHashMark = "!";

    /// <summary>
    /// Клиент сценария: анонимный, без авто-редиректов (точные статусы).
    /// </summary>
    public static HttpClient Create(B17WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// given «пользователь создан прямым DI-сидом в IUserRepository тестового
    /// хоста»: используется как «ДРУГОЙ пользователь» (занятый email/логин);
    /// текущий пользователь сценариев профиля — засеянный преподаватель.
    /// </summary>
    public static User SeedUser(
        B17WebAppFactory factory,
        string login,
        string email,
        string fullName,
        string role,
        Guid? groupId,
        string? password = null)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = email,
            PasswordHash = SeededPasswordHashMark,
            FullName = fullName,
            Role = role,
            GroupId = groupId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        factory.Services.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    /// <summary>given «группа создана DI-сидом IGroupRepository».</summary>
    public static Group SeedGroup(B17WebAppFactory factory, string name)
    {
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        factory.Services.GetRequiredService<IGroupRepository>().AddAsync(group);
        return group;
    }

    /// <summary>
    /// given «текущее состояние групп изменилось»: переименование прямым DI-сидом
    /// IGroupRepository.Update — ответ профиля должен показать НОВОЕ имя
    /// (вычисление по текущему состоянию групп, FR-015).
    /// </summary>
    public static async Task RenameGroupAsync(B17WebAppFactory factory, Group group, string newName)
    {
        await factory.Services.GetRequiredService<IGroupRepository>().UpdateAsync(new Group
        {
            Id = group.Id,
            Name = newName,
            CreatedAt = group.CreatedAt,
        });
    }

    /// <summary>Пользователь по uuid из хранилища тестового хоста (копия-снимок, IF-015).</summary>
    public static async Task<User> StoredUserAsync(B17WebAppFactory factory, Guid userId)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
        Assert.NotNull(user);
        return user!;
    }

    /// <summary>
    /// Текущий пользователь приложения — засеянный преподаватель (Seed__TeacherLogin,
    /// умолчание «teacher»): из хранилища тестового хоста (копия-снимок, IF-015).
    /// </summary>
    public static async Task<User> CurrentUserAsync(B17WebAppFactory factory)
    {
        var user = await factory.Services.GetRequiredService<IUserRepository>()
            .GetByLoginAsync(SeedOptions.DefaultTeacherLogin);
        Assert.NotNull(user);
        return user!;
    }
}
