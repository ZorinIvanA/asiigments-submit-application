using System.Globalization;
using LabsApp.Domain.Entities;
using LabsApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Помощники scope-кейса TS-195 батча B-13: DI-сид студента и анонимный клиент
/// (аутентификации в приложении нет). Работают поверх любого хоста
/// <see cref="WebApplicationFactory{Program}"/> (и B13WebAppFactory, и
/// B13ScopeWebAppFactory). Клиент — без авто-редиректов и без cookie-контейнера.
/// </summary>
public static class B13ScopeHost
{
    public const string StudentsEndpoint = "/api/v1/students";

    /// <summary>Клиент тестового хоста: точные статусы.</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>DI-сид студента напрямую в IUserRepository (ADR-010).</summary>
    public static User AddStudent(WebApplicationFactory<Program> factory, string login)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            // Плейсхолдер сида приложения (SeedRunner): пароли не проверяются.
            PasswordHash = "!",
            FullName = $"Студент {login}",
            Role = UserRoles.Student,
            GroupId = null,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }
}
