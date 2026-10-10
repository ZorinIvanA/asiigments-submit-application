using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-150 «Свои сдачи: semester отсутствует/вне диапазона/нечисловой — 400»
/// (negative, P0, FR-021/ISS-011/AR-002).
///
/// given: student01 авторизован; Labs__MaxSemester=10 (умолчание конфигурации).
/// when:  GET /me/submissions (без semester); затем ?semester=11; затем
///        ?semester=abc.
/// then:  все три — 400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10'] — не 200 с пустыми
///        массивами (AC FR-021 «me/submissions: внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts150_MeSubmissionsSemesterRequiredStrictTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts150_MeSubmissionsSemesterRequiredStrictTests(B06SubmissionsWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithoutOutOfRangeAndNonNumericSemester_Returns400()
    {
        // given: сессия student01.
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");

        // when/then: semester отсутствует — 400 со строгой ошибкой semester.
        using var missing = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: null);
        await AssertStrictSemesterErrorAsync(missing);

        // when/then: semester=11 (вне 1..10) — 400, не 200 с пустыми массивами.
        using var outOfRange = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "11");
        await AssertStrictSemesterErrorAsync(outOfRange);

        // when/then: semester=abc (нечисловой) — 400 с той же ошибкой.
        using var nonNumeric = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "abc");
        await AssertStrictSemesterErrorAsync(nonNumeric);
    }

    /// <summary>400 'Данные заполнены неверно' + errors.semester=['Семестр — число от 1 до 10'].</summary>
    private static async Task AssertStrictSemesterErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "semester", "Семестр — число от 1 до 10");
    }
}
