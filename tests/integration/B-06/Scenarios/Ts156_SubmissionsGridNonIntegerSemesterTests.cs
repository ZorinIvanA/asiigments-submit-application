using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-156 «Ведомость: нечисловой semester — 400 (строгий контракт)»
/// (boundary, P0, FR-021).
///
/// given: teacher; groupId валиден; Labs__MaxSemester=10 (умолчание хоста).
/// when:  GET /api/v1/submissions?groupId=&lt;валидный&gt;&amp;semester=abc;
///        затем ?semester=2.5 (нецелое).
/// then:  оба — 400 'Данные заполнены неверно' + errors.semester
///        (ISS-011/AR-002: отсутствие/нечисловое/нецелое → 400, строгий контракт).
/// </summary>
public sealed class Ts156_SubmissionsGridNonIntegerSemesterTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;
    private readonly string _groupId;

    public Ts156_SubmissionsGridNonIntegerSemesterTests(B06SubmissionsWebAppFactory factory)
    {
        _factory = factory;
        _groupId = B06SubmissionsSessions.RequireGroup(factory, "ИК-221").Id.ToString();
    }

    [Fact]
    public async Task GetGridWithNonNumericAndNonIntegerSemester_Returns400ForBoth()
    {
        // given: Labs__MaxSemester=10 — текст ошибки параметричен конфигурации хоста.
        Assert.Equal(
            LabsOptions.DefaultMaxSemester,
            _factory.Services.GetRequiredService<IOptions<LabsOptions>>().Value.MaxSemester);
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when/then: semester=abc (нечисловой) — 400 со строгой ошибкой semester.
        using var nonNumeric = await B06SubmissionsApi.GetGridAsync(client, _groupId, semester: "abc", page: "1");
        await AssertStrictSemesterErrorAsync(nonNumeric);

        // when/then: semester=2.5 (нецелое) — 400 с той же строгой ошибкой.
        using var nonInteger = await B06SubmissionsApi.GetGridAsync(client, _groupId, semester: "2.5", page: "1");
        await AssertStrictSemesterErrorAsync(nonInteger);
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
