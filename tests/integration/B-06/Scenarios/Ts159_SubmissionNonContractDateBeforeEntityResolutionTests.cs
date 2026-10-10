using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-159 «Upsert сдачи: неконтрактная дата — 400 раньше 404» (негативный, P0, FR-021).
///
/// given: запрос от teacher с датами в неконтрактном формате и неизвестными
///        studentId/labId (случайные uuid).
/// when:  PUT /api/v1/submissions {studentId:&lt;неизвестный&gt;, labId:&lt;неизвестный&gt;,
///        submitDate:'20.09.2026', defenseDate:null}.
/// then:  400 errors.submitDate=['Дата должна быть строкой в формате ГГГГ-ММ-ДД']
///        (не 404 — даты проверяются до разрешения сущностей; FR-021 AC).
/// </summary>
public sealed class Ts159_SubmissionNonContractDateBeforeEntityResolutionTests :
    IClassFixture<B06SubmissionsWebAppFactory>
{
    private readonly B06SubmissionsWebAppFactory _factory;

    public Ts159_SubmissionNonContractDateBeforeEntityResolutionTests(B06SubmissionsWebAppFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task PutSubmissionWithNonContractDate_Returns400BeforeEntityResolution()
    {
        // given: teacher-сессия; studentId/labId — неизвестные (случайные) uuid.
        using var client = B06SubmissionsSessions.CreateTeacherSession(_factory).Client;
        var unknownStudentId = Guid.NewGuid();
        var unknownLabId = Guid.NewGuid();

        // when: PUT /submissions с неконтрактной датой '20.09.2026' и неизвестными сущностями.
        using var response = await B06SubmissionsApi.PutSubmissionAsync(
            client, unknownStudentId.ToString(), unknownLabId.ToString(),
            submitDate: "20.09.2026", defenseDate: null);

        // then: 400 errors.submitDate (даты проверяются ДО разрешения сущностей — не 404).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(
            root, "submitDate", "Дата должна быть строкой в формате ГГГГ-ММ-ДД");
    }
}
