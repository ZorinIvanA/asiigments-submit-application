using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Observability;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-072 «Переотправка кода гасит прежние живые коды» (data_integrity,
/// FR-017 + FR-018, P0).
///
/// given: Development; живой код пользователя существует (извлечён из Email.Dev).
/// when:  повторный POST /auth/recovery/request того же email; затем confirm с
///        прежним кодом.
/// then:  запрос → 200; прежний код получил usedAt; confirm прежним кодом →
///        400 «Код восстановления не подходит». FR-017 AC «Переотправка гасит
///        прежние коды» (одновременно жив максимум один код).
///
/// Прямая инспекция UsedAt прежней записи интерфейсом IF-015 недоступна
/// (перечисления нет); usedAt фиксируется наблюдаемо: после переотправки живой
/// код в хранилище — ДРУГОЙ (новый Id), а прежний код при confirm отвергнут
/// (400), что объяснимо только гашением (TTL 10 минут не истёк).
/// </summary>
public sealed class Ts072_RecoveryResendKillsPreviousCodeTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts072-student";
    private const string Email = "a@b.ru";
    private const string FullName = "Студент СемьдесятДва";
    private const string RequestBody = """{"email":"a@b.ru"}""";

    private readonly B08WebAppFactory _factory;

    public Ts072_RecoveryResendKillsPreviousCodeTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Resend_RecoveryRequest_AnnulsPreviousLiveCode()
    {
        // given: Development; живой код пользователя существует (извлечён из Email.Dev).
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        var codes = _factory.Services.GetRequiredService<ISecurityTokenRepository>();

        using var first = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var firstRecord = _factory.LogSink.OfCategory(DevEmailSender.LogCategory).Single();
        var previousCode = ResponseAssertions.ExtractSixDigitRun(firstRecord);
        Assert.NotNull(previousCode);
        var previousLive = codes.FindLiveForUser(user.Id);
        Assert.NotNull(previousLive);

        // when: повторный POST /auth/recovery/request того же email.
        _factory.LogSink.Clear();
        using var resend = await client.PostAsync(B08Host.RecoveryRequestEndpoint, RequestBody);

        // then: запрос → 200; живой код — ДРУГАЯ запись (прежний погашен, live — максимум один).
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var resentBody = await resend.Content.ReadAsStringAsync();
        ResponseAssertions.AssertEmptyBody(resentBody, "recovery/request (переотправка)");
        var currentLive = codes.FindLiveForUser(user.Id);
        Assert.NotNull(currentLive);
        Assert.True(
            currentLive.Id != previousLive.Id,
            "После переотправки живым должен быть НОВЫЙ код (прежний получил usedAt).");

        // when: confirm с прежним кодом.
        var confirmBody = $$"""{"email":"{{Email}}","code":"{{previousCode}}"}""";
        using var confirm = await client.PostAsync(B08Host.RecoveryConfirmEndpoint, confirmBody);

        // then: 400 «Код восстановления не подходит» (прежний код погашен usedAt).
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        var confirmResponseBody = await confirm.Content.ReadAsStringAsync();
        ResponseAssertions.AssertMessageEquals(confirmResponseBody, B08Host.CodeRejectedMessage, "recovery/confirm прежним кодом");
    }
}
