using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-177 «labs: граница длины content 500/501» (boundary, P1, FR-017/FR-023).
///
/// given: сессия teacher (сид-преподаватель демо-набора); пары (semester=1,
///        number=211) и (semester=1, number=212) свободны (демо-сид создаёт
///        работы семестра 1 №1–20 и семестра 2 №1–3); тестируемый content без
///        пробельного обрамления ('x'×N) — граница проверяется на длине после
///        трима без двусмысленности.
/// when:  POST /api/v1/labs {number:211, semester:1, content:'x'×500,
///        assignmentUrl:null, defenseRequired:false}; отдельно POST {number:212,
///        semester:1, content:'x'×501, assignmentUrl:null, defenseRequired:false}.
/// then:  первый — 201 (ровно 500 символов валидно, запись (1,211) создана);
///        второй — 400 «Данные заполнены неверно», errors.content ровно
///        ['Содержание — от 1 до 500 символов'] — единственный текст ошибки
///        содержания из словаря «Текстов ошибок полей» (lab.content; FR-017:
///        «content — 1–500 после трима», FR-023); лабораторная с number=212
///        не создана.
///
/// Примечание зоны: текущая редакция кейса (переиздание a-132 по сверке
/// дерева) закрепляет зону-владельца tests/integration/B-06 — зона test_zone
/// батча B-06; прежнее закрепление за B-04 снято (файлов Ts177 в B-04 нет).
/// Файл размещён в зоне батча, when/then исполнены дословно.
/// </summary>
public sealed class Ts177_LabContentBoundaryTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts177_LabContentBoundaryTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateLab_Content500Created_Content501RejectedWithDictionaryText()
    {
        // given: сессия teacher; пары (semester=1, number=211) и (semester=1,
        // number=212) свободны.
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        Assert.Null(labs.TryGetByPair(semester: 1, number: 211));
        Assert.Null(labs.TryGetByPair(semester: 1, number: 212));
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: POST /labs {number:211, semester:1, content:'x'×500,
        // assignmentUrl:null, defenseRequired:false}.
        using var created = await client.PostAsJsonAsync(B06SubmissionsApi.LabsEndpoint, new
        {
            number = 211,
            semester = 1,
            content = new string('x', 500),
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 201 — ровно 500 символов валидно; запись (1,211) создана.
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(labs.TryGetByPair(semester: 1, number: 211));

        // when: отдельный POST /labs {number:212, semester:1, content:'x'×501,
        // assignmentUrl:null, defenseRequired:false}.
        using var tooLong = await client.PostAsJsonAsync(B06SubmissionsApi.LabsEndpoint, new
        {
            number = 212,
            semester = 1,
            content = new string('x', 501),
            assignmentUrl = (string?)null,
            defenseRequired = false,
        });

        // then: 400 «Данные заполнены неверно», errors.content ровно
        // ['Содержание — от 1 до 500 символов']; лабораторная (1,212) не создана.
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(tooLong);
        BodyAssertions.MessageIs(body, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(body, "content", "Содержание — от 1 до 500 символов");
        Assert.Null(labs.TryGetByPair(semester: 1, number: 212));
    }
}
