using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-054 «Успешный вход не считается и не блокируется лимитом» (idempotency,
/// FR-013, P1).
///
/// given: 4 неуспешных попытки в окне по ключу (teacher, 10.0.0.1).
/// when:  вход с верным паролем тем же ключом; затем ещё одна неуспешная; затем
///        ещё одна неуспешная.
/// then:  верный вход → 200; следующая неуспешная → 401 (5-я метка допускается);
///        ещё одна → 429 (6-я). FR-013 AC «Успех не блокируется»: «успешный вход
///        лимитом не блокируется и не считается».
/// </summary>
public sealed class Ts054_LoginSuccessNotCountedTests : IClassFixture<B07AuthWebAppFactory>
{
    private const string ClientIp = "10.0.0.1";

    private readonly B07AuthWebAppFactory _factory;

    public Ts054_LoginSuccessNotCountedTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SuccessBetweenFailures_IsNotCounted()
    {
        // given: 4 неуспешных попытки в окне по ключу (teacher, 10.0.0.1).
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var failure = await B07AuthClients.PostLoginAsync(client, "teacher", "wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // when: вход с верным паролем тем же ключом.
        using var success = await B07AuthClients.PostLoginAsync(client, "teacher", B07AuthWebAppFactory.TeacherPassword);

        // then: 200 — успешный вход не блокируется лимитом.
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        // when: затем ещё одна неуспешная попытка тем же ключом.
        using var fifth = await B07AuthClients.PostLoginAsync(client, "teacher", "wrong");

        // then: 401 — 5-я метка в окне допускается (успех не считался).
        Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);

        // when: ещё одна неуспешная попытка тем же ключом.
        using var sixth = await B07AuthClients.PostLoginAsync(client, "teacher", "wrong");

        // then: 429 — 6-я неуспешная попытка окна.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }
}
