using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-097 (P0, negative; FR-017 AC «Редактирование без самоконфликта»,
/// FR-024 инвариант уникальности (semester, number)) «Лабораторные: update —
/// самоконфликт 200, чужая пара 409, невалидное тело 400 (в т.ч. раньше 404),
/// несуществующий id 404».
/// given: существуют работы L1 (semester=1, number=5) и L2 (semester=1, number=6) —
///        записи с РАЗНЫМИ парами (DI-сид); сессия teacher.
/// when:  PUT /labs/{L1.id} с теми же semester=1, number=5 и новым
///        content='Обновлённое содержание'; затем PUT /labs/{L1.id} с парой
///        semester=1, number=6 (занята записью L2); затем PUT /labs/{L1.id} с
///        пакетно невалидным телом {number:'0', semester:0, content:'x'×501,
///        assignmentUrl:'ftp://x', defenseRequired:true}; отдельно PUT /labs/&lt;несуществующий-uuid&gt;
///        с тем же невалидным телом; отдельно PUT /labs/&lt;несуществующий-uuid&gt; с валидным телом.
/// then:  первый — 200 (не 409 — собственная запись конфликтом не считается);
///        второй — 409 'Лабораторная с таким номером уже есть в семестре' (пара занята
///        ДРУГОЙ записью); третий — 400 'Данные заполнены неверно' с одновременными
///        errors.number/errors.semester=['Семестр — число от 1 до 10']/errors.content/
///        errors.assignmentUrl (PUT наследует валидацию LabInput от POST), запись L1
///        не изменена (content равен значению, записанному шагом 1); четвёртый — те же
///        400, не 404 (порядок отказов 401→403→400→404→409); пятый — 404
///        'Лабораторная не найдена'.
/// </summary>
public sealed class Ts097_LabPutContractTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    /// <summary>Значение content, записанное шагом 1 и защищённое от изменения шагом 3.</summary>
    private const string RenewedContent = "Обновлённое содержание";

    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS097_PutLab_SelfPairUpdates_ForeignPairConflicts_ValidationBefore404()
    {
        // given: L1 (1, 5) и L2 (1, 6) — разные записи с разными парами; teacher.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var l1 = B04DomainSeed.AddLab(labs, semester: 1, number: 5);
        _ = B04DomainSeed.AddLab(labs, semester: 1, number: 6);
        using var client = MintedSessions.CreateTeacherClient(_factory);

        // when/then (1): PUT собственной записи с той же парой (1, 5) — 200, не 409.
        using var selfUpdate = await client.PutAsJsonAsync($"/api/v1/labs/{l1.Id}", new
        {
            number = 5,
            semester = 1,
            content = RenewedContent,
        });
        var updated = await ApiAssert.ReadOkJsonAsync(selfUpdate);
        Assert.Equal(l1.Id.ToString(), updated.GetProperty("id").GetString());
        Assert.Equal(RenewedContent, updated.GetProperty("content").GetString());

        // when/then (2): PUT пары (1, 6), занятой ДРУГОЙ записью L2 — 409 CONFLICT_LAB.
        using var foreignPair = await client.PutAsJsonAsync($"/api/v1/labs/{l1.Id}", new
        {
            number = 6,
            semester = 1,
            content = RenewedContent,
        });
        await ApiAssert.AssertMessageAsync(
            foreignPair,
            HttpStatusCode.Conflict,
            "Лабораторная с таким номером уже есть в семестре");

        // when (3): PUT /labs/{L1.id} с пакетно невалидным телом.
        using var invalidOnExisting = await client.PutAsJsonAsync($"/api/v1/labs/{l1.Id}", InvalidLabBody());

        // then (3): 400 — PUT наследует валидацию LabInput от POST, ВСЕ ошибки поля
        // одновременно; запись L1 не изменена (content равен значению шага 1).
        await AssertLabValidationPackageAsync(invalidOnExisting);
        using var unchanged = await client.GetAsync($"/api/v1/labs/{l1.Id}");
        var unchangedRoot = await ApiAssert.ReadOkJsonAsync(unchanged);
        Assert.Equal(RenewedContent, unchangedRoot.GetProperty("content").GetString());
        Assert.Equal(5, unchangedRoot.GetProperty("number").GetInt32());
        Assert.Equal(1, unchangedRoot.GetProperty("semester").GetInt32());

        // when/then (4): то же невалидное тело по несуществующему uuid — те же 400,
        // НЕ 404 (валидация раньше поиска записи: порядок 400→404).
        using var invalidOnUnknown = await client.PutAsJsonAsync(
            $"/api/v1/labs/{Guid.NewGuid()}", InvalidLabBody());
        await AssertLabValidationPackageAsync(invalidOnUnknown);

        // when/then (5): валидное тело по несуществующему uuid — 404 NOT_FOUND_LAB.
        using var validOnUnknown = await client.PutAsJsonAsync($"/api/v1/labs/{Guid.NewGuid()}", new
        {
            number = 99,
            semester = 2,
            content = "Валидное содержание",
        });
        await ApiAssert.AssertMessageAsync(
            validOnUnknown, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }

    /// <summary>Пакетно невалидное тело LabInput из стимула кейса (все поля нарушают правила).</summary>
    private static object InvalidLabBody() => new
    {
        number = "0",
        semester = 0,
        content = new string('x', 501),
        assignmentUrl = "ftp://x",
        defenseRequired = true,
    };

    /// <summary>
    /// 400 'Данные заполнены неверно' с ОДНОВРЕМЕННЫМИ ошибками четырёх полей —
    /// дословные тексты словаря; errors.semester параметризован Labs__MaxSemester=10.
    /// </summary>
    private static async Task AssertLabValidationPackageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Данные заполнены неверно", root.GetProperty("message").GetString());
        ApiAssert.HasExactlyProperties(root, "message", "errors");

        var errors = root.GetProperty("errors");
        ApiAssert.HasExactlyProperties(errors, "number", "semester", "content", "assignmentUrl");
        Assert.Equal("Номер должен быть положительным числом", SingleErrorText(errors, "number"));
        Assert.Equal("Семестр — число от 1 до 10", SingleErrorText(errors, "semester"));
        Assert.Equal("Содержание — от 1 до 500 символов", SingleErrorText(errors, "content"));
        Assert.Equal(
            "Ссылка должна начинаться с http:// или https://",
            SingleErrorText(errors, "assignmentUrl"));
    }

    /// <summary>Единственный текст errors.{field} (массив ровно из одного элемента).</summary>
    private static string SingleErrorText(JsonElement errors, string field)
    {
        var texts = errors.GetProperty(field);
        Assert.Equal(1, texts.GetArrayLength());
        return texts[0].GetString()!;
    }
}
