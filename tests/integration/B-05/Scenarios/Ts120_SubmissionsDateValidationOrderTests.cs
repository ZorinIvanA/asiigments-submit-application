using System.Text;
using LabsApp.IntegrationTests.B05.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-120 «submissions: формат дат submitDate И defenseDate проверяется ДО
/// разрешения сущностей; 404-ветки» (negative, FR-021).
///
/// СКЕП2-ISS-003: добавлены стимулы ветки errors.defenseDate — во всём прежнем
/// реестре defenseDate передавался только null либо календарно корректная дата,
/// ветка контракта PUT /api/v1/submissions errors{submitDate|defenseDate} не
/// стимулировалась ни одним невалидным значением.
///
/// given: сессия teacher; известны несуществующие studentId и labId.
/// when:  PUT {studentId:&lt;неизвестный&gt;, labId:&lt;неизвестный&gt;,
///        submitDate:'20.09.2026', defenseDate:null}; отдельно PUT с
///        submitDate:'2026-02-30' (defenseDate:null); отдельно PUT с валидным
///        submitDate:'2026-09-20' и defenseDate:'21.09.2026' (неконтрактный
///        формат даты); отдельно PUT с валидным submitDate:'2026-09-20' и
///        defenseDate:'2026-02-30' (формат YYYY-MM-DD, но календарно
///        некорректная); отдельно PUT с валидными обеими датами и неизвестным
///        studentId; отдельно с валидными датами и неизвестным labId.
/// then:  первый — 400 errors.submitDate=['Дата должна быть строкой в формате
///        ГГГГ-ММ-ДД'] (не 404); '2026-02-30' в submitDate — 400 с той же
///        ошибкой по submitDate (календарная корректность); оба стимула с
///        валидным submitDate и неконтрактным defenseDate ('21.09.2026' и
///        '2026-02-30') — 400 'Данные заполнены неверно' с
///        errors.defenseDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД']
///        — ключ ошибки именно defenseDate, errors.submitDate в этих ответах
///        отсутствует (дефект привязки валидации к полю, неверный ключ
///        errors-карты либо пропуск проверки defenseDate обнаруживается);
///        с валидными датами и неизвестным студентом — 404 'Студент не найден';
///        с неизвестной работой — 404 'Лабораторная не найдена'.
/// </summary>
public sealed class Ts120_SubmissionsDateValidationOrderTests : IClassFixture<B05WebAppFactory>
{
    private const string GridEndpoint = "/api/v1/submissions";
    private const string DateInvalidText = "Дата должна быть строкой в формате ГГГГ-ММ-ДД";
    private const string ValidationMessage = "Данные заполнены неверно";

    private readonly B05WebAppFactory _factory;

    public Ts120_SubmissionsDateValidationOrderTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NonContractSubmitDateFormat_IsRejected400BeforeEntityResolution()
    {
        // given: неизвестные studentId/labId; teacher.
        var unknownStudent = Guid.NewGuid();
        var unknownLab = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: неконтрактный формат '20.09.2026' при обеих неизвестных сущностях.
        using var wrongFormat = await client.PutAsync(
            GridEndpoint,
            PutBody(unknownStudent, unknownLab, "\"submitDate\":\"20.09.2026\""));

        // then: 400 (не 404) с errors.submitDate — формат проверяется раньше
        //       разрешения сущностей.
        Assert.Equal(HttpStatusCode.BadRequest, wrongFormat.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(wrongFormat);
        BodyAssertions.MessageIs(root, ValidationMessage);
        B05ContractAsserts.SingleFieldErrorIs(root, "submitDate", DateInvalidText);
    }

    [Fact]
    public async Task NonCalendarSubmitDate_IsRejectedWithSameFormatError()
    {
        // given: неизвестные studentId/labId; teacher.
        var unknownStudent = Guid.NewGuid();
        var unknownLab = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: календарно некорректная дата '2026-02-30' в контрактном формате.
        using var nonCalendar = await client.PutAsync(
            GridEndpoint,
            PutBody(unknownStudent, unknownLab, "\"submitDate\":\"2026-02-30\""));

        // then: 400 с той же ошибкой формата по submitDate (календарная
        //       корректность).
        Assert.Equal(HttpStatusCode.BadRequest, nonCalendar.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(nonCalendar);
        BodyAssertions.MessageIs(root, ValidationMessage);
        B05ContractAsserts.SingleFieldErrorIs(root, "submitDate", DateInvalidText);
    }

    [Fact]
    public async Task ValidSubmitDate_NonContractDefenseDate_IsRejectedOnDefenseDateKeyOnly()
    {
        // given: неизвестные studentId/labId (формат defenseDate проверяется
        //        раньше разрешения сущностей); teacher.
        var unknownStudent = Guid.NewGuid();
        var unknownLab = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: валидный submitDate и defenseDate в неконтрактном формате
        //       '21.09.2026' (точки вместо дефисов).
        using var response = await client.PutAsync(
            GridEndpoint,
            PutBody(unknownStudent, unknownLab, "\"submitDate\":\"2026-09-20\",\"defenseDate\":\"21.09.2026\""));

        // then: 400 'Данные заполнены неверно' с errors.defenseDate — ключ
        //       ошибки именно defenseDate, errors.submitDate в ответе
        //       отсутствует (валидный submitDate не даёт ошибки).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, ValidationMessage);
        Assert.True(
            root.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        BodyAssertions.HasExactlyProperties(errors, "defenseDate");
        B05ContractAsserts.SingleFieldErrorIs(root, "defenseDate", DateInvalidText);
    }

    [Fact]
    public async Task ValidSubmitDate_NonCalendarDefenseDate_IsRejectedOnDefenseDateKeyOnly()
    {
        // given: неизвестные studentId/labId; teacher.
        var unknownStudent = Guid.NewGuid();
        var unknownLab = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: валидный submitDate и defenseDate '2026-02-30' — формат
        //       YYYY-MM-DD, но календарно некорректная дата.
        using var response = await client.PutAsync(
            GridEndpoint,
            PutBody(unknownStudent, unknownLab, "\"submitDate\":\"2026-09-20\",\"defenseDate\":\"2026-02-30\""));

        // then: 400 'Данные заполнены неверно' с errors.defenseDate (календарная
        //       корректность defenseDate); errors.submitDate отсутствует.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, ValidationMessage);
        Assert.True(
            root.TryGetProperty("errors", out var errors),
            "В теле 400 отсутствует ключ errors.");
        BodyAssertions.HasExactlyProperties(errors, "defenseDate");
        B05ContractAsserts.SingleFieldErrorIs(root, "defenseDate", DateInvalidText);
    }

    [Fact]
    public async Task ValidDates_UnknownStudent_Returns404StudentNotFound()
    {
        // given: неизвестный studentId; существующая работа 1:1; валидные даты.
        var unknownStudent = Guid.NewGuid();
        var lab = _factory.Services.GetRequiredService<ILabRepository>().TryGetByPair(1, 1)
            ?? throw new InvalidOperationException(
                "Работа 1:1 не найдена в демо-сиде — given кейса неисполним.");
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: PUT с валидными датами и неизвестным студентом.
        using var response = await client.PutAsync(
            GridEndpoint,
            PutBody(unknownStudent, lab.Id, "\"submitDate\":\"2026-09-20\",\"defenseDate\":null"));

        // then: 404 'Студент не найден'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Студент не найден");
    }

    [Fact]
    public async Task ValidDates_UnknownLab_Returns404LabNotFound()
    {
        // given: существующий студент student05; неизвестный labId; валидные даты.
        var student05 = B05SeedLookup.StudentByLogin(_factory, "student05");
        var unknownLab = Guid.NewGuid();
        using var client = B05MintedSessions.CreateTeacherClient(_factory);

        // when: PUT с валидными датами и неизвестной работой.
        using var response = await client.PutAsync(
            GridEndpoint,
            PutBody(student05.Id, unknownLab, "\"submitDate\":\"2026-09-20\",\"defenseDate\":null"));

        // then: 404 'Лабораторная не найдена'.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Лабораторная не найдена");
    }

    /// <summary>Тело PUT /submissions с дословными JSON-фрагментами дат.</summary>
    private static StringContent PutBody(Guid studentId, Guid labId, string dateFragment) =>
        new(
            "{\"studentId\":\"" + studentId + "\",\"labId\":\"" + labId + "\"," + dateFragment + "}",
            Encoding.UTF8,
            "application/json");
}
