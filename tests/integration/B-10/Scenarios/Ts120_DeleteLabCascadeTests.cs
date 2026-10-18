using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-120 (P0, data_integrity; FR-034) «DELETE /labs/{id}: каскадное удаление сдач».
/// given: Лаба X семестра 1 со сдачами двух студентов (DI-сид: работа (1,1), группа
///        и два её студента со сдачами по X, ADR-010); сессия teacher; группа студентов известна.
/// when: DELETE /labs/X; повторный DELETE /labs/X; GET /labs/X; инспекция репозитория
///        сдач и GET /submissions.
/// then: Первый → 204; повторный → 404; GET /labs/X → 404; записей с labId=X в
///        репозитории сдач нет; в ведомости семестра записи по X отсутствуют
///        (FR-034 AC «Каскад»).
/// </summary>
public sealed class Ts120_DeleteLabCascadeTests(B10NoDemoWebAppFactory factory) : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS120_DeleteLab_RemovesLabAndAllItsSubmissions()
    {
        // given: DI-сид (ADR-010): лаба X=(1,1); группа; два её студента со сдачами по X.
        var labX = B10Seed.AddLab(_factory, semester: 1, number: 1).Id;
        var groupId = B10Seed.AddGroup(_factory, "Б-10-ДиСид").Id;
        var student1 = B10Seed.AddStudent(_factory, "b10cascade01", groupId);
        var student2 = B10Seed.AddStudent(_factory, "b10cascade02", groupId);
        B10Seed.AddSubmission(
            _factory, student1.Id, labX, submitDate: new DateOnly(2026, 9, 1), defenseDate: null);
        B10Seed.AddSubmission(
            _factory, student2.Id, labX, submitDate: new DateOnly(2026, 9, 2), defenseDate: new DateOnly(2026, 9, 3));
        var before = B10Seed.SubmissionsByLab(_factory, labX);
        Assert.True(
            before.Count == 2 && before.Select(submission => submission.StudentId).Distinct().Count() == 2,
            $"Шаг given неисполним: у работы X ожидались сдачи двух студентов, фактически {before.Count}.");
        using var client = HostClients.CreateTeacherClient(_factory);

        // when: DELETE /labs/X.
        using var deleted = await client.DeleteAsync($"/api/v1/labs/{labX}");

        // then: первый → 204.
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // then: повторный DELETE → 404.
        using var repeated = await client.DeleteAsync($"/api/v1/labs/{labX}");
        await ApiAssert.AssertMessageAsync(
            repeated,
            HttpStatusCode.NotFound,
            "Лабораторная не найдена",
            exactSingleMessageProperty: true);

        // then: GET /labs/X → 404.
        using var missing = await client.GetAsync($"/api/v1/labs/{labX}");
        await ApiAssert.AssertMessageAsync(
            missing,
            HttpStatusCode.NotFound,
            "Лабораторная не найдена",
            exactSingleMessageProperty: true);

        // then: записей с labId=X в репозитории сдач нет.
        Assert.Empty(B10Seed.SubmissionsByLab(_factory, labX));

        // then: в ведомости семестра (GET /submissions) записи по X отсутствуют.
        var grid = await B10Seed.ReadAllGridSubmissionsAsync(client, groupId, semester: 1);
        Assert.DoesNotContain(
            grid,
            submission => string.Equals(submission.LabId, labX.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
