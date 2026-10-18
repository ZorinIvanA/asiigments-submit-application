using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-152 (P1, boundary; FR-021/IF-001, v2.2 ASM-018) «GET /submissions:
/// нецелочисленный semester — строгий 400 с errors.semester».
/// given: Группа X со студентами (и работа со сдачей в семестре 1).
/// when: GET /api/v1/submissions?groupId=X&amp;semester=abc.
/// then: 400 «Данные заполнены неверно» c errors.semester «Семестр — число от
///       1 до 10» (асимметрия ASM-018: /submissions — строгая валидация semester,
///       в отличие от мягкой нормализации GET /labs).
/// </summary>
public sealed class Ts152_SubmissionsNonIntegerSemesterTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS152_NonIntegerSemester_Returns400WithSemesterFieldError()
    {
        // given: группа с двумя студентами; в семестре 1 работа и сдача одного из них.
        var group = B13Seed.AddGroup(_factory, "Группа TS-152");
        var first = B13Seed.AddStudent(_factory, "ts152-s1", "Алексеев", group.Id);
        B13Seed.AddStudent(_factory, "ts152-s2", "Борисов", group.Id);
        var lab = B13Seed.AddLab(_factory, 1, 1);
        B13Seed.AddSubmission(_factory, first.Id, lab.Id, "2025-03-01", null);
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: GET /submissions?groupId=X&semester=abc.
        using var response = await client.GetAsync(
            $"/api/v1/submissions?groupId={Uri.EscapeDataString(group.Id.ToString())}&semester=abc");

        // then: 400 «Данные заполнены неверно» c errors.semester
        //       «Семестр — число от 1 до 10» (строгая валидация ASM-018).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiAssert.ReadJsonAsync(response);
        Assert.Equal("Данные заполнены неверно", body.GetProperty("message").GetString());
        var semesterErrors = body.GetProperty("errors").GetProperty("semester");
        Assert.Equal(1, semesterErrors.GetArrayLength());
        Assert.Equal("Семестр — число от 1 до 10", semesterErrors[0].GetString());
    }
}
