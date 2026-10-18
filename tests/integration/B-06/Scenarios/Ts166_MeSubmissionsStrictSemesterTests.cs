using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-166 «me/submissions: внедиапазонный/нечисловой semester — 400»
/// (boundary, P0, FR-021).
///
/// given: student01 авторизован; Labs__MaxSemester=10 (умолчание хоста).
/// when:  GET /api/v1/me/submissions?semester=11; затем ?semester=abc.
/// then:  оба — 400 'Данные заполнены неверно' +
///        errors.semester=['Семестр — число от 1 до 10'] (не 200 с пустыми
///        массивами; FR-021 AC «me/submissions: внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts166_MeSubmissionsStrictSemesterTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts166_MeSubmissionsStrictSemesterTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMeSubmissionsWithOutOfRangeAndNonNumericSemester_Returns400()
    {
        // given: student01 авторизован; Labs__MaxSemester=10 — текст ошибки параметричен конфигурации.
        Assert.Equal(
            LabsOptions.DefaultMaxSemester,
            _factory.Services.GetRequiredService<IOptions<LabsOptions>>().Value.MaxSemester);
        using var client = B06SubmissionsSessions.CreateStudentSessionByLogin(_factory, "student01");

        // when/then: semester=11 (вне 1..10) — 400, не 200 с пустыми массивами.
        using var outOfRange = await B06SubmissionsApi.GetMeSubmissionsAsync(client, semester: "11");
        await AssertStrictSemesterErrorAsync(outOfRange);

        // when/then: semester=abc (нечисловой) — 400 с той же строгой ошибкой.
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
