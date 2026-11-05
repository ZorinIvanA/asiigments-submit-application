using System.Text.Json;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-121 «Форма тела ошибок: строго {message, errors?} без лишних полей»
/// (nfr, FR-024, NFR-007).
///
/// given: анонимный доступ (текущий пользователь — сид-преподаватель); демо-сид
///        (работа (1,1) существует).
/// when:  POST /api/v1/labs {number:0, semester:1, content:'x', assignmentUrl:null,
///        defenseRequired:false} (400); POST /api/v1/labs {number:1, semester:1, ...}
///        (409); GET /api/v1/labs/{нуль-uuid} (404).
/// then:  тело 400 — объект ровно с ключами message и errors; message='Данные
///        заполнены неверно'; каждое значение errors — непустой массив строк
///        (русские тексты); тела 409 и 404 — объект ровно {message} с
///        соответствующим дословным текстом словаря, без errors и дополнительных
///        полей. FR-024: «Тело любой ошибки API — JSON {message: string,
///        errors?: {поле: string[]}}…»; AC «Форма тела ошибки валидации»,
///        «409 с текстом словаря».
/// </summary>
public sealed class Ts121_ErrorBodyShapeTests : IClassFixture<B05WebAppFactory>
{
    private const string LabsEndpoint = "/api/v1/labs";
    private const string ZeroLabId = "00000000-0000-0000-0000-000000000000";

    private readonly B05WebAppFactory _factory;

    public Ts121_ErrorBodyShapeTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Validation400_BodyIsExactlyMessageAndErrorsWithRussianTexts()
    {
        // given: анонимный доступ (текущий пользователь — сид-преподаватель).
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/labs с невалидным number:0 (остальные поля валидны).
        using var response = await client.PostAsJsonAsync(LabsEndpoint, new
        {
            number = 0,
            semester = 1,
            content = "x",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 400; тело ровно {message, errors}; message дословно;
        // каждое значение errors — непустой массив непустых русских строк.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");

        Assert.True(
            root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object,
            "Ожидался объект errors в теле 400 валидации.");
        var fieldKeys = errors.EnumerateObject().ToList();
        Assert.True(
            fieldKeys.Count > 0,
            "Ожидался непустой объект errors в теле 400 валидации.");
        foreach (var field in fieldKeys)
        {
            BodyAssertions.ErrorFieldIsNonEmptyStringArray(root, field.Name, requireCyrillic: true);
        }
    }

    [Fact]
    public async Task Conflict409_BodyIsExactlyDictionaryMessage()
    {
        // given: анонимный доступ; демо-сид — работа (1,1) существует.
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/labs с дублирующей парой (1,1).
        using var response = await client.PostAsJsonAsync(LabsEndpoint, new
        {
            number = 1,
            semester = 1,
            content = "Дубликат пары",
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: HTTP 409; тело ровно {message} без errors и лишних полей, текст словаря.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Лабораторная с таким номером уже есть в семестре");
    }

    [Fact]
    public async Task NotFound404_BodyIsExactlyDictionaryMessage()
    {
        // given: анонимный доступ.
        using var client = HostClients.Create(_factory);

        // when: GET /api/v1/labs/{нуль-uuid}.
        using var response = await client.GetAsync($"{LabsEndpoint}/{ZeroLabId}");

        // then: HTTP 404; тело ровно {message}, текст словаря «Лабораторная не найдена».
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }
}
