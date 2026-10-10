using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Сессии и данные сида кейсов волны сдач B-06 (ADR-015): пользователи демо-набора
/// (student01/05/31, сид-преподаватель «teacher») берутся из IUserRepository
/// тестового хоста, access-JWT минтится ITokenService хоста и передаётся заголовком
/// Cookie с именем-константой AuthCoreDefaults (единый источник с ICookieService).
/// POST /auth/login не используется (ADR-015: доменные кейсы волны не зависят от
/// auth-эндпоинтов). Сущности сида (группа ИК-221, работы пар) читаются из DI-репозиториев.
/// </summary>
public static class B06SubmissionsSessions
{
    /// <summary>
    /// given «сессия teacher; uuid преподавателя известен» (TS-147/TS-148/TS-155):
    /// клиент с access-cookie сид-преподавателя и его uuid.
    /// </summary>
    public static (HttpClient Client, Guid TeacherId) CreateTeacherSession(
        WebApplicationFactory<Program> factory)
    {
        var teacher = RequireUser(factory, SeedOptions.DefaultTeacherLogin);
        return (CreateSessionClient(factory, teacher), teacher.Id);
    }

    /// <summary>
    /// given «сессия студента <paramref name="login"/>» демо-набора
    /// (TS-152: student31; TS-153/TS-154: student01; TS-155: student-сессия).
    /// </summary>
    public static HttpClient CreateStudentSessionByLogin(
        WebApplicationFactory<Program> factory,
        string login)
    {
        var student = RequireUser(factory, login);
        Assert.Equal(UserRoles.Student, student.Role);
        return CreateSessionClient(factory, student);
    }

    /// <summary>Учётная запись демо-набора по логину либо падение шага given.</summary>
    public static User RequireUser(WebApplicationFactory<Program> factory, string login) =>
        factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
        ?? throw new InvalidOperationException(
            $"Пользователь «{login}» не найден в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");

    /// <summary>Группа демо-набора по имени (ИК-221) либо падение шага given.</summary>
    public static Group RequireGroup(WebApplicationFactory<Program> factory, string name) =>
        factory.Services.GetRequiredService<IGroupRepository>().GetByName(name)
        ?? throw new InvalidOperationException(
            $"Группа «{name}» не найдена в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");

    /// <summary>Работа демо-набора по паре (семестр, номер) либо падение шага given.</summary>
    public static Lab RequireLab(WebApplicationFactory<Program> factory, int semester, int number) =>
        factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(semester, number)
        ?? throw new InvalidOperationException(
            $"Работа {semester}:{number} не найдена в DI-хранилище тестового хоста (демо-набор сида не развёрнут; шаг given неисполним).");

    private static HttpClient CreateSessionClient(WebApplicationFactory<Program> factory, User user)
    {
        var accessToken = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role);
        return HostClients.CreateWithCookie(
            factory, AuthCoreDefaults.AccessTokenCookieName, accessToken);
    }
}
