using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-149 «Upsert сдачи: неконтрактная дата — 400 раньше 404» (негативный, P0, FR-021/FR-022).
///
/// given: сессия teacher; studentId и labId указывают на несуществующие сущности;
///        submitDate='20.09.2026' (не строка формата ГГГГ-ММ-ДД).
/// when:  PUT /api/v1/submissions {studentId:&lt;неизвестный&gt;, labId:&lt;неизвестный&gt;,
///        submitDate:'20.09.2026', defenseDate:null}.
/// then:  400; errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД'] —
///        НЕ 404 (дата-поля проверяются до разрешения сущностей; FR-021 AC
///        «Неконтрактная дата — 400 раньше 404»).
/// </summary>
public sealed class Ts149_SubmissionBadDateBeforeLookupTests : IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts149_SubmissionBadDateBeforeLookupTests(B06SubmissionsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutSubmissionWithNonContractDate_Returns400BeforeEntityLookup()
    {
        // given: сессия teacher; обе сущности не существуют (случайные uuid).
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: PUT /submissions с неконтрактной датой и неизвестными сущностями.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client,
            studentId: Guid.NewGuid().ToString(),
            labId: Guid.NewGuid().ToString(),
            submitDate: "20.09.2026",
            defenseDate: null);

        // then: 400 с errors.submitDate (дата-поля раньше разрешения сущностей), НЕ 404.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
