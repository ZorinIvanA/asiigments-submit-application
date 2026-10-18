using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-011 «Реальный статический файл отдаётся напрямую» (FR-002, P2).
/// given: wwwroot/favicon.ico существует.
/// when: GET /favicon.ico.
/// then: 200; тело равно содержимому файла; Content-Type image/x-icon.
/// (В предыдущей нумерации зоны кейс значился как TS-008 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts011_StaticFileDirectTests
{
    [Fact]
    public async Task GetFaviconIco_ServesFileContentWithIconMimeType()
    {
        // given: wwwroot/favicon.ico существует (TestAssets копируется csproj-целью).
        var filePath = Path.Combine(RepoPaths.TestWwwroot, "favicon.ico");
        Assert.True(File.Exists(filePath), "Тестовый wwwroot/favicon.ico не найден — фикстура кейса сломана.");

        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when: GET /favicon.ico — существующий файл статики.
        using var response = await client.GetAsync("/favicon.ico");

        // then: 200; тело = содержимое файла; MIME image/x-icon.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/x-icon", response.Content.Headers.ContentType?.MediaType);
        var expected = await File.ReadAllBytesAsync(filePath);
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.True(
            expected.AsSpan().SequenceEqual(actual),
            $"Тело /favicon.ico не совпадает с файлом ({expected.Length} байт), получено {actual.Length} байт.");
    }
}
