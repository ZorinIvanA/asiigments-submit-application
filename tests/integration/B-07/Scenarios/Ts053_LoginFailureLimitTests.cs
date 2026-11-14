using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-053 «Лимит неудач входа 5/мин по (login_ci, IP): 6-я → 429» (boundary,
/// FR-013 + FR-080, P0).
///
/// given: RemoteIpAddress=10.0.0.1; выполнено 5 неуспешных входов
///        {login:'teacher', password:'wrong'} подряд.
/// when:  6-я неуспешная попытка с теми же login и IP; отдельно 6-я с
///        RemoteIpAddress=10.0.0.2 и тем же логином; отдельно с login='other'
///        и IP=10.0.0.1.
/// then:  6-я тем же ключом → 429 {"message":"Слишком много попыток. Повторите
///        позже"}; с другим IP → 401; с другим логином → 401. FR-013 AC
///        «Лимит неудач»; FR-080 AC «Вход по (login, IP)».
/// </summary>
public sealed class Ts053_LoginFailureLimitTests : IClassFixture<B07AuthWebAppFactory>
{
    private const string PrimaryIp = "10.0.0.1";
    private const string OtherIp = "10.0.0.2";

    private readonly B07AuthWebAppFactory _factory;

    public Ts053_LoginFailureLimitTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthFailureSameKey429_OtherIpAndOtherLogin401()
    {
        // given: RemoteIpAddress=10.0.0.1; 5 неуспешных входов (teacher, wrong) подряд.
        using var primary = B07AuthClients.CreateClientWithIp(_factory, PrimaryIp);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failure = await B07AuthClients.PostLoginAsync(primary, "teacher", "wrong");
            Assert.Equal(
                HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        // when: 6-я неуспешная попытка с теми же login и IP.
        using var sixth = await B07AuthClients.PostLoginAsync(primary, "teacher", "wrong");

        // then: 429 {"message":"Слишком много попыток. Повторите позже"}.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, "Слишком много попыток. Повторите позже");

        // when: отдельно 6-я с RemoteIpAddress=10.0.0.2 и тем же логином.
        using var otherIpClient = B07AuthClients.CreateClientWithIp(_factory, OtherIp);
        using var fromOtherIp = await B07AuthClients.PostLoginAsync(otherIpClient, "teacher", "wrong");

        // then: 401 — другой ключ лимитера (другой IP).
        Assert.Equal(HttpStatusCode.Unauthorized, fromOtherIp.StatusCode);

        // when: отдельно с login='other' и IP=10.0.0.1.
        using var fromOtherLogin = await B07AuthClients.PostLoginAsync(primary, "other", "wrong");

        // then: 401 — другой ключ лимитера (другой логин).
        Assert.Equal(HttpStatusCode.Unauthorized, fromOtherLogin.StatusCode);
    }
}
