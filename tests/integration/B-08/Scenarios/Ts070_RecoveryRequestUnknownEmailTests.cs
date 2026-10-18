using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-070 «Запрос кода для неизвестного email: 200, записей нет, неотличимо»
/// (negative, FR-017, P0).
///
/// given: email x@y.zz нет в хранилище.
/// when:  POST /auth/recovery/request {email:'x@y.zz'}; инспекция RecoveryCode.
/// then:  200 {}; новых записей RecoveryCode не создано. Неотличимость по ВРЕМЕНИ
///        верифицируется выделенным гейтом KdfCallCounter задачи T-009
///        (ADR-023: ровно один HashRecoveryCode в обеих ветках) — в зоне батча
///        проверяется форма ответа: статус/тело совпадают с кейсом TS-069.
///        FR-017 AC «Неизвестный email».
/// </summary>
public sealed class Ts070_RecoveryRequestUnknownEmailTests : IClassFixture<B08WebAppFactory>
{
    private const string UnknownEmail = "x@y.zz";
    private const string RequestBody = """{"email":"x@y.zz"}""";

    private readonly B08WebAppFactory _factory;

    public Ts070_RecoveryRequestUnknownEmailTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RecoveryRequest_UnknownEmail_ReturnsEmptyObjectAndStoresNothing()
    {
        // given: email x@y.zz нет в хранилище; число записей RecoveryCode зафиксировано.
        using var client = B08Host.CreateClient(_factory);
        Assert.Null(_factory.Services.GetRequiredService<IUserRepository>().GetByEmail(UnknownEmail));
        var recordsBefore = B08Host.CountRecoveryCodeRecords(_factory);

        // when: POST /auth/recovery/request {email:'x@y.zz'}.
        using var response = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);

        // then: 200 {}.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        ResponseAssertions.AssertEmptyBody(body, "recovery/request (неизвестный email)");

        // then: новых записей RecoveryCode не создано.
        var recordsAfter = B08Host.CountRecoveryCodeRecords(_factory);
        Assert.True(
            recordsAfter == recordsBefore,
            $"Число записей RecoveryCode изменилось: было {recordsBefore}, стало {recordsAfter} "
            + "(запрос неизвестного email не должен создавать записи, FR-017 AC «Неизвестный email»).");
    }
}
