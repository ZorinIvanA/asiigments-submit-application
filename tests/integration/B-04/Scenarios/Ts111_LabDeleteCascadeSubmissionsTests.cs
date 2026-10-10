using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-111 (P0, happy_path; FR-017 AC «Удаление каскадно», FR-021) «Лабораторные:
/// удаление каскадно удаляет сдачи».
/// given: есть сдача по работе L (создана PUT /api/v1/submissions); teacher.
/// when:  DELETE /labs/{L.id}; затем GET /labs/{L.id} и GET /submissions?groupId
///        &amp;semester со страницей, где была сдача.
/// then:  204; записей сдач с labId=L.id нет (каскад); GET /labs/{L.id} — 404.
/// </summary>
public sealed class Ts111_LabDeleteCascadeSubmissionsTests(B04LabsWebAppFactory factory)
    : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS111_DeleteLab_RemovesItsSubmissionsFromGrid()
    {
        // given: группа с одним студентом, работа L (семестр 3, №7) и сдача по ней,
        // созданная PUT /api/v1/submissions; teacher. DI-сид — через B04DomainSeed
        // (метки времени фиксированы SeedMoment — детерминизм, CR-003).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();

        var group = B04DomainSeed.AddGroup(groups, "Б-04 ИК-111");
        var student = B04DomainSeed.AddStudent(users, "b04-ts111-student", group.Id);
        var lab = B04DomainSeed.AddLab(labs, semester: 3, number: 7);

        using var client = MintedSessions.CreateTeacherClient(_factory);

        using var created = await client.PutAsJsonAsync("/api/v1/submissions", new
        {
            studentId = student.Id,
            labId = lab.Id,
            submitDate = "2026-09-20",
            defenseDate = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // then (до удаления): в хранилище сдач по работе L ровно одна запись —
        // созданная сдача. Точка наблюдения — ISubmissionRepository из
        // factory.Services (образец Ts099): HTTP-ведомость не годится как
        // единственный оракул каскада — контроллер собирает её из хранилища
        // работ, и при сломанном каскаде осиротевшая запись всё равно не
        // попала бы в выдачу (CR-001).
        var beforeDelete = submissions.ListByLabIds([lab.Id]);
        var record = Assert.Single(beforeDelete);
        Assert.Equal(student.Id, record.StudentId);
        Assert.Equal(lab.Id, record.LabId);

        // when: DELETE /labs/{L.id}.
        using var delete = await client.DeleteAsync($"/api/v1/labs/{lab.Id}");

        // then: 204; записей сдач с labId=L.id в хранилище больше нет
        // (каскад, проверено напрямую).
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(submissions.ListByLabIds([lab.Id]));

        // then: и на странице ведомости, где сдача была (страница 1: студент —
        // единственный в группе), записей с labId=L.id нет (HTTP-срез каскада).
        using var grid = await client.GetAsync(
            $"/api/v1/submissions?groupId={group.Id}&semester=3&page=1");
        var gridRoot = await ApiAssert.ReadOkJsonAsync(grid);

        var gridSubmissions = gridRoot.GetProperty("submissions");
        Assert.Equal(JsonValueKind.Array, gridSubmissions.ValueKind);
        foreach (var submission in gridSubmissions.EnumerateArray())
        {
            Assert.NotEqual(lab.Id.ToString(), submission.GetProperty("labId").GetString());
        }

        // then: GET /labs/{L.id} — 404 'Лабораторная не найдена'.
        using var get = await client.GetAsync($"/api/v1/labs/{lab.Id}");
        await ApiAssert.AssertMessageAsync(get, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
