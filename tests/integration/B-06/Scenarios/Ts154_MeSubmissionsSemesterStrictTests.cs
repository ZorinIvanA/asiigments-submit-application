using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-154 «me/submissions: внедиапазонный/нечисловой semester — 400» (boundary, P0, FR-021).
///
/// given: student01 авторизован (демо-сид); Labs__MaxSemester=10 (умолчание конфигурации).
/// when:  GET /api/v1/me/submissions?semester=11 и GET /api/v1/me/submissions?semester=abc.
/// then:  оба — 400; message 'Данные заполнены неверно';
///        errors.semester=['Семестр — число от 1 до 10'] — не 200 с пустыми массивами
///        (FR-021 AC «me/submissions: внедиапазонный semester — 400», ISS-011/AR-002).
/// </summary>
public sealed class Ts154_MeSubmissionsSemesterStrictTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts154_MeSubmissionsSemesterStrictTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithOutOfRangeAndNonNumericSemester_Returns400()
    {
        // given: сессия student01.
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");

        // when/then: semester=11 — 400 со строгой ошибкой semester.
        using var outOfRange = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "11");
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        var outOfRangeRoot = await BodyAssertions.ReadRootObjectAsync(outOfRange);
        BodyAssertions.MessageIs(outOfRangeRoot, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(outOfRangeRoot, "semester", "Семестр — число от 1 до 10");

        // when/then: semester=abc — 400 с той же ошибкой (не 200 с пустыми массивами).
        using var nonNumeric = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "abc");
        Assert.Equal(HttpStatusCode.BadRequest, nonNumeric.StatusCode);
        var nonNumericRoot = await BodyAssertions.ReadRootObjectAsync(nonNumeric);
        BodyAssertions.MessageIs(nonNumericRoot, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(nonNumericRoot, "semester", "Семестр — число от 1 до 10");
    }
}
