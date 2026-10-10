using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-163 «Пустой search — без фильтра (эквивалент отсутствия параметра)»
/// (boundary, FR-019).
///
/// given: teacher авторизован; демо-сид (32 студента).
/// when:  GET /api/v1/students (без параметра search); затем ?search= (пустое
///        значение); затем ?search=%20%20 (только пробелы, URL-encoded).
/// then:  все три ответа — HTTP 200 с total=32, page=1 и идентичными массивами
///        items (первая страница полной выборки fullName↑, login↑): пустая и
///        пробельная строка после трима дают ноль токенов — фильтр не
///        применяется. FR-019: «пустой/отсутствующий search — без фильтра».
/// </summary>
public sealed class Ts163_EmptySearchNoFilterTests : IClassFixture<B05WebAppFactory>
{
    private const string StudentsEndpoint = "/api/v1/students";

    private readonly B05WebAppFactory _factory;

    public Ts163_EmptySearchNoFilterTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task MissingEmptyAndWhitespaceSearch_ProduceIdenticalUnfilteredPages()
    {
        // given: teacher авторизован; демо-сид.
        using var client = await HostClients.CreateTeacherClientAsync(_factory);

        // when: три варианта search — отсутствует, пустой, только пробелы (%20%20).
        using var withoutSearch = await client.GetAsync(StudentsEndpoint);
        using var withEmptySearch = await client.GetAsync($"{StudentsEndpoint}?search=");
        using var withWhitespaceSearch = await client.GetAsync($"{StudentsEndpoint}?search=%20%20");

        // then: все три — HTTP 200, total=32, page=1, items идентичны.
        Assert.All(
            new[] { withoutSearch, withEmptySearch, withWhitespaceSearch },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var withoutRoot = await BodyAssertions.ReadRootObjectAsync(withoutSearch);
        var emptyRoot = await BodyAssertions.ReadRootObjectAsync(withEmptySearch);
        var whitespaceRoot = await BodyAssertions.ReadRootObjectAsync(withWhitespaceSearch);

        Assert.All(
            new[] { withoutRoot, emptyRoot, whitespaceRoot },
            root => Assert.Equal(32, root.GetProperty("total").GetInt32()));
        Assert.All(
            new[] { withoutRoot, emptyRoot, whitespaceRoot },
            root => Assert.Equal(1, root.GetProperty("page").GetInt32()));

        var withoutItems = ItemsRawText(withoutRoot);
        Assert.Equal(withoutItems, ItemsRawText(emptyRoot));
        Assert.Equal(withoutItems, ItemsRawText(whitespaceRoot));

        // Демо-сид гарантирует непустую первую страницу — без этой проверки
        // сравнение items выполнялось бы на пустых массивах (CR-002).
        var items = withoutRoot.GetProperty("items");
        Assert.True(
            items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0,
            "Ожидалась непустая первая страница студентов (демо-сид, 32 записи).");
    }

    /// <summary>Канонический текст массива items — для поэлементного сравнения выборок.</summary>
    private static string ItemsRawText(JsonElement root) =>
        root.GetProperty("items").GetRawText();
}
