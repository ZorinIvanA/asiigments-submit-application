using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-008 «SPA fallback: deep-link отдаёт index.html» (FR-002, P0).
/// given: wwwroot/index.html существует (тестовая копия с маркерным содержимым).
/// when: GET /works и GET /groups/abc без cookie.
/// then: оба — 200; Content-Type text/html; тело байт-в-байт равно содержимому
/// wwwroot/index.html.
/// (В предыдущей нумерации зоны кейс значился как TS-005 — файл переименован
/// по актуальному набору кейсов батча.)
/// </summary>
public sealed class Ts008_SpaFallbackDeepLinkTests
{
    internal static async Task AssertDeepLinkServesIndexAsync(HttpClient client, string path)
    {
        // when: GET deep-link без cookie.
        using var response = await client.GetAsync(path);

        // then: 200; Content-Type text/html; тело = wwwroot/index.html байт-в-байт.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var expected = await File.ReadAllBytesAsync(Path.Combine(RepoPaths.TestWwwroot, "index.html"));
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.True(
            expected.AsSpan().SequenceEqual(actual),
            $"Тело {path} не совпадает байт-в-байт с wwwroot/index.html "
            + $"(ожидалось {expected.Length} байт, получено {actual.Length}).");
    }

    [Fact]
    public async Task GetWorks_ServesIndexHtmlByteForByte()
    {
        // given: wwwroot/index.html существует (TestAssets копируется csproj-целью
        // в content root тестового хоста).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when/then.
        await AssertDeepLinkServesIndexAsync(client, "/works");
    }

    [Fact]
    public async Task GetGroupsAbc_ServesIndexHtmlByteForByte()
    {
        // given: wwwroot/index.html существует.
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);

        // when/then.
        await AssertDeepLinkServesIndexAsync(client, "/groups/abc");
    }
}
