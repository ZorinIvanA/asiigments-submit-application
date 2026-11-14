using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy47 (P1, interruption; FR-007, FR-023) «Вход: обрыв тела посреди передачи —
/// без KDF, сервер жив». Кейс СТАРОГО реестра зоны (бывший TS-047), сохранён вне
/// актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под однозначным
/// ID LegacyNN — новый батч этот сценарий не покрывает (CR-001 раунда 2026-10-10;
/// фильтры по каноническим Ts-номерам на Legacy-классы не попадают).
/// given: Хост запущен; счётчик KDF сброшен.
/// when:  POST /auth/login с оборванной передачей тела (соединение разорвано
///        посреди тела запроса).
/// then:  Обработка отклоняется без выполнения KDF (Δkdf=0); допустимые исходы —
///        400 'Данные заполнены неверно' либо отказ на уровне транспорта строго
///        транспортных типов (HttpRequestException / IOException /
///        OperationCanceledException); нетранспортные исключения тестом не
///        поглощаются и роняют тест (фикс a-085/CR-003 против ложнозелёного
///        проглатывания дефектов фикстуры); сервер остаётся работоспособен —
///        последующий GET /health возвращает 200.
/// </summary>
public sealed class Legacy47_LoginTruncatedBodyTests(B11WebAppFactory factory) : IClassFixture<B11WebAppFactory>
{
    private const string HealthPath = "/health";

    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy47_Login_TruncatedBodyTransfer_RejectsWithoutKdfAndHostStaysAlive()
    {
        // given: хост запущен; счётчик KDF сброшен снимком.
        _ = _factory.Services;
        var kdf = B11Kdf.Resolve(_factory.Services);
        var before = kdf.Snapshot();
        using var client = HostClients.Create(_factory);

        // when: POST /auth/login — передача тела обрывается посреди JSON
        // (Content-Length заявлен больше переданного, поток завершается отказом).
        HttpResponseMessage? response = null;
        Exception? transportFailure = null;
        try
        {
            response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, HostClients.LoginPath)
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

        var after = kdf.Snapshot();

        // then: обработка отклонена без выполнения KDF — Δkdf=0.
        Assert.Equal(0L, B11Kdf.TotalDelta(before, after));

        // then: ответ 400 'Данные заполнены неверно' ЛИБО отказ на уровне транспорта
        // строго транспортных типов (кейс допускает оба исхода обрыва; прочие
        // исключения не поглощаются — фильтр catch выше, иной HTTP-статус — падение).
        if (transportFailure is null)
        {
            Assert.NotNull(response);
            _ = await ApiAssert.AssertMessageAsync(
                response!,
                HttpStatusCode.BadRequest,
                "Данные заполнены неверно",
                exactSingleMessageProperty: true);
        }

        // then: сервер остаётся работоспособен — последующий GET /health — 200.
        using var health = await client.GetAsync(HealthPath);
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
            throw new IOException("Legacy47: соединение разорвано посреди передачи тела запроса.");
        }

        protected override bool TryComputeLength(out long length)
        {
            // Заявленный размер больше фактически передаваемого — обрыв, а не конец тела.
            length = 128;
            return true;
        }
    }
}
