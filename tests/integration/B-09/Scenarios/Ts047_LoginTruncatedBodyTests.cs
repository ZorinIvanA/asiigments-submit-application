using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-047 (P2, interruption; FR-007, FR-023) «Вход: обрыв тела посреди передачи —
/// без KDF, сервер жив».
/// given: Хост запущен; счётчик KDF сброшен.
/// when:  POST /auth/login с оборванной передачей тела (соединение разорвано
///        посреди тела запроса).
/// then:  Обработка отклоняется без выполнения KDF (Δkdf=0; ответ 400 'Данные
///        заполнены неверно' либо отказ на уровне транспорта СТРОГО транспортных
///        типов HttpRequestException / IOException / OperationCanceledException —
///        нетранспортные исключения тестом не поглощаются и роняют тест, фикс
///        a-085/CR-003 против ложнозелёного проглатывания дефектов фикстуры);
///        сервер остаётся работоспособен — последующий GET /health возвращает 200.
/// </summary>
public sealed class Ts047_LoginTruncatedBodyTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.47";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS047_Login_TruncatedBodyTransfer_RejectsWithoutKdfAndHostStaysAlive()
    {
        // given: хост запущен; счётчик KDF сброшен.
        _ = _factory.Services;
        var before = B09KdfSeams.KdfSnapshot(_factory);
        using var client = B09AuthHttp.Create(_factory, TestIp);

        // when: POST /auth/login — передача тела обрывается посреди JSON
        // (Content-Length заявлен больше переданного, поток завершается отказом).
        HttpResponseMessage? response = null;
        Exception? transportFailure = null;
        try
        {
            response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, B09AuthHttp.LoginPath)
            {
                Content = new TruncatedJsonContent(),
            });
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or OperationCanceledException)
        {
            transportFailure = exception;
        }

        var after = B09KdfSeams.KdfSnapshot(_factory);

        // then: обработка отклонена без выполнения KDF — Δkdf=0.
        Assert.Equal(0L, B09KdfSeams.DeltaTotal(before, after));

        // then: ответ 400 'Данные заполнены неверно' ЛИБО отказ на уровне транспорта
        // (кейс допускает оба исхода обрыва; иной HTTP-статус — расхождение с кейсом).
        if (transportFailure is null)
        {
            Assert.NotNull(response);
            var body = await B09Assertions.ParseObjectAsync(
                response!, HttpStatusCode.BadRequest, "TS-047: оборванное тело");
            B09Assertions.MessageIs(body, "Данные заполнены неверно");
        }

        // then: сервер остаётся работоспособен — последующий GET /health возвращает 200.
        using var health = await client.GetAsync(B09AuthHttp.HealthPath);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    /// <summary>
    /// Тело, передача которого обрывается посреди JSON: несколько валидных байт,
    /// затем поток завершается исключением (соединение разорвано).
    /// </summary>
    private sealed class TruncatedJsonContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = """{"login":"teacher","pass"""u8.ToArray();
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            throw new IOException("TS-047: соединение разорвано посреди передачи тела запроса.");
        }

        protected override bool TryComputeLength(out long length)
        {
            // Заявленный размер больше фактически передаваемого — обрыв, а не конец тела.
            length = 128;
            return true;
        }
    }
}
