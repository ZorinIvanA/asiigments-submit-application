using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-154 «Словарь §8: все message воспроизводятся дословно» (nfr, NFR-007, FR-024).
///
/// given: свежий экземпляр приложения (Development, демо-сид); параметры для
///        триггеров каждого сообщения.
/// when:  параметрический контрактный прогон (по одному [Fact] на триггер):
///        «Лабораторная с таким номером уже есть в семестре», «Лабораторная не
///        найдена», «Группа с таким названием уже существует», «Группа не
///        найдена», «Студент не найден», «Не найдено»; триггеры сообщений
///        аутентификации (вход/регистрация/пароль/восстановление) и ролевых
///        отказов 401/403 удалены вместе с аутентификацией (раунд де-авторизации).
/// then:  для каждого триггера фактическое поле message совпадает с эталонной
///        строкой побайтно (включая пробелы, точки и регистр). NFR-007: «Все
///        пользовательские тексты message — дословно из словаря §8»; глоссарий
///        «Словарь ошибок API (§8)».
/// </summary>
public sealed class Ts154_DictionaryMessageTests : IClassFixture<B05WebAppFactory>
{
    private const string ZeroId = "00000000-0000-0000-0000-000000000000";

    private readonly B05WebAppFactory _factory;

    public Ts154_DictionaryMessageTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DuplicateLab_PostExistingPair()
    {
        // given: анонимный доступ; работа (1,1) существует (демо-сид).
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/labs с дублирующей парой.
        using var response = await client.PostAsJsonAsync("/api/v1/labs", new
        {
            number = 1,
            semester = 1,
            content = "Словарь: дубликат пары",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 409, message «Лабораторная с таким номером уже есть в семестре» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная с таким номером уже есть в семестре");
    }

    [Fact]
    public async Task LabNotFound_PutUnknownLabId()
    {
        // given: анонимный доступ.
        using var client = HostClients.Create(_factory);

        // when: PUT /api/v1/labs/{нуль-uuid} с валидными полями.
        using var response = await client.PutAsJsonAsync($"/api/v1/labs/{ZeroId}", new
        {
            number = 1,
            semester = 5,
            content = "Словарь: нет работы",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 404, message «Лабораторная не найдена» дословно.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }

    [Fact]
    public async Task DuplicateGroup_PostExistingName()
    {
        // given: анонимный доступ; группа ИК-221 существует (демо-сид).
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/groups с занятым названием.
        using var response = await client.PostAsJsonAsync("/api/v1/groups", new { name = "ИК-221" });

        // then: HTTP 409, message «Группа с таким названием уже существует» дословно.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа с таким названием уже существует");
    }

    [Fact]
    public async Task GroupNotFound_GetStudentsOfUnknownGroup()
    {
        // given: анонимный доступ.
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/groups/{нуль-uuid}/students.
        using var response = await client.GetAsync($"/api/v1/groups/{ZeroId}/students");

        // then: HTTP 404, message «Группа не найдена» дословно.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Группа не найдена");
    }

    [Fact]
    public async Task StudentNotFound_PutSubmissionWithUnknownStudent()
    {
        // given: анонимный доступ; id существующей работы (1,1) из демо-сида.
        using var client = HostClients.Create(_factory);
        var labId = await FindLabIdAsync(client, semester: 1, number: 1);

        // when: PUT /api/v1/submissions с несуществующим studentId.
        using var response = await client.PutAsJsonAsync("/api/v1/submissions", new
        {
            studentId = ZeroId,
            labId,
            submitDate = "2026-10-05",
            defenseDate = (string?)null,
        });

        // then: HTTP 404, message «Студент не найден» дословно
        // (порядок 404: студент раньше работы).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task NotFound_UnknownApiRoute()
    {
        // given: свежий экземпляр.
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/nonexistent.
        using var response = await client.GetAsync("/api/v1/nonexistent");

        // then: HTTP 404, message «Не найдено» дословно (технический 404 вне §8, NFR-007).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Не найдено");
    }

    /// <summary>id работы по паре (semester, number) из списка лабораторных (FR-013).</summary>
    private static async Task<string> FindLabIdAsync(HttpClient client, int semester, int number)
    {
        using var response = await client.GetAsync($"/api/v1/labs?semester={semester}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        var match = root.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("number").GetInt32() == number);
        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"Предусловие кейса: работа ({semester},{number}) не найдена в демо-сиде.");
        return match.GetProperty("id").GetString()!;
    }
}
