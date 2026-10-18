using LabsApp.IntegrationTests.B15.Groups.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Groups.Scenarios;

/// <summary>
/// TS-119 «Группы: границы длины имени» (boundary, FR-019, P1).
///
/// given: сессия teacher; имена свободны (демо-сид граничных имён не содержит;
///        имена без пробельных символов — после трима длина сохраняется).
/// when:  POST /groups {name:&lt;ровно 100 символов&gt;}; POST {name:&lt;101 символ&gt;}.
/// then:  100 — 201; 101 — 400 errors.name=['Название группы — от 1 до 100
///        символов'] (граница 1–100 после трима, FR-019/IF-010).
///
/// Ожидание кейса «пробельное имя — 400 errors.name=['Заполните поле']»
/// противоречит тексту самого FR-019 («name 1–100 после трима, ИНАЧЕ 400
/// errors.name=['Название группы — от 1 до 100 символов']» — единый текст для
/// любого значения вне 1–100, включая пустое/пробельное) и приземлённому
/// контракту IF-010 (пустая/пробельная строка невалидна тем же единым текстом);
/// ссылка кейса на комментарий клиентского словаря error-texts.ts («пустое —
/// это required») не описывает серверный конверт FR-023/IF-010. Дефект кейса
/// возвращён конвейеру через scenario_change_requests — эта часть кейса в тест
/// не включена (запрет на переосмысление кейса тестером).
/// </summary>
public sealed class Ts119_GroupNameLengthBoundaryTests : IClassFixture<B15GroupsDemoDataFactory>
{
    private const string NameValidationText = "Название группы — от 1 до 100 символов";

    private readonly B15GroupsDemoDataFactory _factory;

    public Ts119_GroupNameLengthBoundaryTests(B15GroupsDemoDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PostGroup_NameOf100CharsCreated_NameOf101Rejected()
    {
        // given: сессия teacher; имена граничной длины свободны.
        using var client = B15GroupsSession.CreateTeacher(_factory);
        var name100 = new string('Г', 100);
        var name101 = new string('Г', 101);

        // when: POST /groups {name:<ровно 100 символов>}.
        using var created = await B15GroupsApi.PostGroupAsync(client, name100);

        // then: 201.
        using var createdBody = await B15GroupsApi.ParseWithStatusAsync(
            created, HttpStatusCode.Created, "POST /groups {name:100 символов} (teacher)");
        Assert.Equal(name100, B15GroupsApi.ReadString(createdBody.RootElement, "name"));

        // when: POST /groups {name:<101 символ>}.
        using var rejected = await B15GroupsApi.PostGroupAsync(client, name101);

        // then: 400 errors.name=['Название группы — от 1 до 100 символов'].
        using var rejectedBody = await B15GroupsApi.ParseWithStatusAsync(
            rejected, HttpStatusCode.BadRequest, "POST /groups {name:101 символ} (teacher)");
        B15GroupsApi.FieldErrorsExactly(rejectedBody.RootElement, "name", NameValidationText);
    }
}
