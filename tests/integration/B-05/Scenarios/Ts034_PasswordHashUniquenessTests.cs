using LabsApp.Auth;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-034 (P0, nfr; FR-010, NFR-005) «Одинаковые пароли дают разные хэши
/// (уникальные соли)».
///
/// given: две регистрации с одинаковым паролем 'Passw0rd!' (разные login/email),
///        с разных IP (10.2.0.1 и 10.2.0.2).
/// when:  инспекция двух passwordHash.
/// then:  хэши различаются (разные случайные соли); оба
///        verify('Passw0rd!')=true. NFR-005: «хэш уникален для одинаковых
///        паролей (разные соли)».
/// </summary>
public sealed class Ts034_PasswordHashUniquenessTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS034_SamePasswordTwice_ProducesDifferentHashesBothVerifying()
    {
        // given: две регистрации с одним паролем, разные login/email и разные IP.
        using var first = B05SecurityClients.CreateClientWithIp(_factory, "10.2.0.1");
        using var second = B05SecurityClients.CreateClientWithIp(_factory, "10.2.0.2");

        using var registration1 = await B05SecurityClients.PostRegisterAsync(
            first, "Студент ТриДцатьЧетыре А", "ts034-user-a", "ts034-user-a@example.com");
        Assert.True(
            registration1.StatusCode == HttpStatusCode.Created,
            $"Предусловие кейса: первая регистрация должна вернуть 201, фактически {registration1.StatusCode}: " +
            $"{await registration1.Content.ReadAsStringAsync()}");

        using var registration2 = await B05SecurityClients.PostRegisterAsync(
            second, "Студент ТриДцатьЧетыре Б", "ts034-user-b", "ts034-user-b@example.com");
        Assert.True(
            registration2.StatusCode == HttpStatusCode.Created,
            $"Предусловие кейса: вторая регистрация должна вернуть 201, фактически {registration2.StatusCode}: " +
            $"{await registration2.Content.ReadAsStringAsync()}");

        // when: инспекция двух passwordHash.
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var hashA = users.GetByLogin("ts034-user-a")?.PasswordHash;
        var hashB = users.GetByLogin("ts034-user-b")?.PasswordHash;
        Assert.False(
            string.IsNullOrEmpty(hashA) || string.IsNullOrEmpty(hashB),
            "Предусловие кейса: оба пользователя обязаны иметь passwordHash в хранилище.");

        // then: хэши различаются (разные случайные соли); оба verify=true.
        Assert.NotEqual(hashA, hashB);
        B05SecurityClients.AssertStoredKdfFormat(hashA!, "passwordHash первого пользователя TS-034");
        B05SecurityClients.AssertStoredKdfFormat(hashB!, "passwordHash второго пользователя TS-034");

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        Assert.True(hasher.Verify(hashA!, B05SecurityClients.CasePassword),
            "Первый хэш обязан верифицировать пароль 'Passw0rd!'.");
        Assert.True(hasher.Verify(hashB!, B05SecurityClients.CasePassword),
            "Второй хэш обязан верифицировать тот же пароль 'Passw0rd!'.");
    }
}
