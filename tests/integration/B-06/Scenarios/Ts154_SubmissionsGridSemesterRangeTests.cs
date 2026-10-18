using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-154 «Ведомость: внедиапазонный semester — 400 (AR-002)» (boundary, P0, FR-021).
///
/// given: teacher; Labs__MaxSemester=10 (умолчание конфигурации хоста); groupId валиден.
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=0;
///        затем ?semester=99.
/// then:  оба — 400 'Данные заполнены неверно',
///        errors.semester=['Семестр — число от 1 до 10'] — НЕ 200 с пустыми
///        массивами (FR-021 AC «Внедиапазонный semester — 400»).
/// </summary>
public sealed class Ts154_SubmissionsGridSemesterRangeTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly string _groupId;

    public Ts154_SubmissionsGridSemesterRangeTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _groupId = B06SubmissionsSessions.RequireGroup(factory, "ИК-221").Id.ToString();
    }

    [Fact]
    public async Task GetGridWithOutOfRangeSemester_Returns400ForBothBounds()
    {
        // given: Labs__MaxSemester=10 — текст ошибки параметричен конфигурации хоста.
        Assert.Equal(
            LabsOptions.DefaultMaxSemester,
            _factory.Services.GetRequiredService<IOptions<LabsOptions>>().Value.MaxSemester);
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when/then: semester=0 (ниже 1) — 400, не 200 с пустыми массивами.
        using var belowRange = await B06SubmissionsApi.GetGridAsync(client, _groupId, semester: "0", page: "1");
        await AssertStrictSemesterErrorAsync(belowRange);

        // when/then: semester=99 (выше 10) — 400 с той же строгой ошибкой.
        using var aboveRange = await B06SubmissionsApi.GetGridAsync(client, _groupId, semester: "99", page: "1");
        await AssertStrictSemesterErrorAsync(aboveRange);
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
