using LabsApp.Auth;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-033 (P0, nfr; FR-010, NFR-005) «Хэш пароля: PBKDF2-HMAC-SHA256,
/// 600000 итераций, верификация».
///
/// given: выполнена регистрация с паролем 'Passw0rd!' (POST /auth/register — 201).
/// when:  инспекция IUserRepository: чтение passwordHash пользователя; вызов
///        верификатора IPasswordHasher.
/// then:  хэш ≠ 'Passw0rd!'; формат хранит параметры (алгоритм PBKDF2-SHA256,
///        600000 итераций, соль ≥16 байт); verify('Passw0rd!', hash)=true;
///        verify('Passw0rd', hash)=false. FR-010 AC «Хэш пароля»; NFR-005.
/// </summary>
public sealed class Ts033_PasswordHashTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private const string Login = "ts033-user";
    private const string Email = "ts033-user@example.com";
    private const string FullName = "Студент ТриДцатьТри";

    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS033_RegisteredPasswordHash_IsParameterizedPbkdf2AndVerifies()
    {
        // given: регистрация с паролем 'Passw0rd!'.
        using var client = B05SecurityClients.CreateClient(_factory);
        using var registration = await B05SecurityClients.PostRegisterAsync(
            client, FullName, Login, Email, B05SecurityClients.CasePassword);
        Assert.True(
            registration.StatusCode == HttpStatusCode.Created,
            $"Предусловие кейса: регистрация должна вернуть 201, фактически {registration.StatusCode}: " +
            $"{await registration.Content.ReadAsStringAsync()}");

        // when: чтение passwordHash пользователя из IUserRepository.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetByLogin(Login);
        Assert.True(
            stored is not null,
            "Предусловие кейса: зарегистрированный пользователь обязан находиться в IUserRepository.");
        var passwordHash = stored!.PasswordHash;

        // then: хэш ≠ пароль (пароль не хранится); формат хранит параметры.
        Assert.NotEqual(B05SecurityClients.CasePassword, passwordHash);
        B05SecurityClients.AssertStoredKdfFormat(passwordHash, "passwordHash пользователя TS-033");

        // then: verify('Passw0rd!', hash)=true; verify('Passw0rd', hash)=false.
        Assert.True(hasher.Verify(passwordHash, B05SecurityClients.CasePassword),
            "Верификация исходного пароля 'Passw0rd!' обязана дать true.");
        Assert.False(hasher.Verify(passwordHash, "Passw0rd"),
            "Верификация неверного пароля 'Passw0rd' обязана дать false.");
    }
}
