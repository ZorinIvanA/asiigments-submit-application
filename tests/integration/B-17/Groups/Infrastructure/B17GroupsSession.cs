using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B17.Groups.Infrastructure;

/// <summary>
/// Сессии «teacher»/«student» доменных кейсов групп батча B-17 (ADR-015, given
/// «сессия teacher» TS-122..TS-128, TS-202, TS-204): access-JWT минтится
/// ITokenService тестового хоста для сид-пользователя (учётки существуют при
/// любом Seed__DemoData: преподаватель — всегда, студенты — в демо-наборе) и
/// передаётся заголовком Cookie с именем-константой AuthCoreDefaults — единый
/// источник с ICookieService. POST /api/v1/auth/login не используется:
/// эндпоинты auth-семейства — чужая волна, доменные кейсы групп от них не
/// зависят (ADR-015).
/// </summary>
public static class B17GroupsSession
{
    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента).
    /// </summary>
    public static HttpClient CreateTeacher(WebApplicationFactory<Program> factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден в DI-хранилище тестового хоста (шаг given «сессия teacher» неисполним).");

        return CreateClientFor(factory, teacher);
    }

    /// <summary>
    /// Клиент с сессией сид-студента по логину (демо-сид: student01..student32).
    /// </summary>
    public static HttpClient CreateStudent(WebApplicationFactory<Program> factory, string login)
    {
        var student = factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(login)
            ?? throw new InvalidOperationException(
                $"Сид-студент «{login}» не найден в DI-хранилище тестового хоста (шаг given сессии студента неисполним).");

        return CreateClientFor(factory, student);
    }

    private static HttpClient CreateClientFor(WebApplicationFactory<Program> factory, LabsApp.Domain.Entities.User user)
    {
        var accessJwt = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        Assert.True(
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={accessJwt}"),
            "Не удалось добавить access-cookie сессии в заголовки тестового клиента.");

        return client;
    }
}
