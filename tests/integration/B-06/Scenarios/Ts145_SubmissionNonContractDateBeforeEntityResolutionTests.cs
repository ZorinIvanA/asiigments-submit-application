using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-145 «Сдачи: неконтрактная дата — 400 раньше 404» (negative, P0, FR-021).
///
/// given: teacher; studentId и labId неизвестны; даты в неконтрактном формате.
/// when:  PUT /api/v1/submissions {studentId:&lt;неизвестный&gt;, labId:&lt;неизвестный&gt;,
///        submitDate:'20.09.2026', defenseDate:null}.
/// then:  400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД'] —
///        дата-поля проверяются ДО разрешения сущностей (не 404)
///        (AC FR-021 «Неконтрактная дата — 400 раньше 404»).
/// </summary>
public sealed class Ts145_SubmissionNonContractDateBeforeEntityResolutionTests :
    IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts145_SubmissionNonContractDateBeforeEntityResolutionTests(B06SubmissionsWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task PutSubmissionWithNonContractDate_Returns400BeforeEntityLookup()
    {
        // given: teacher-сессия; studentId и labId неизвестны (случайные uuid).
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;

        // when: PUT /submissions с неконтрактной датой '20.09.2026' и неизвестными сущностями.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client,
            studentId: Guid.NewGuid().ToString(),
            labId: Guid.NewGuid().ToString(),
            submitDate: "20.09.2026",
            defenseDate: null);

        // then: 400 (не 404) с errors.submitDate словарным текстом даты.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
