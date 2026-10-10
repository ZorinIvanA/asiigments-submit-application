using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-112 (P0, data_integrity; FR-017 AC «Удаление каскадно», FR-021) «Лабораторные:
/// удаление каскадно».
/// given: есть сдача по работе L (создана PUT /submissions).
/// when:  DELETE /labs/{L.id}.
/// then:  204; записей сдач с labId=L.id нет (проверка через шов хранилища);
///        GET /labs/{L.id} — 404.
/// </summary>
public sealed class Ts112_LabDeleteCascadeTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS112_DeleteLabWithSubmission_RemovesSubmissionCascade()
    {
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var teacher = users.GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given кейса неисполним.");
        var student = B04DomainSeed.AddStudent(users, "b04-ts112-student");
        var lab = B04DomainSeed.AddLab(labs, semester: 3, number: 7);

        using var client = MintedSessions.CreateTeacherClient(_factory);

        // given: сдача по работе L создана PUT /submissions.
        using var created = await client.PutAsJsonAsync("/api/v1/submissions", new
        {
            studentId = student.Id,
            labId = lab.Id,
            submitDate = "2026-09-20",
            defenseDate = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Single(submissions.ListByLabIds([lab.Id]));

        // when: DELETE /labs/{L.id}.
        using var delete = await client.DeleteAsync($"/api/v1/labs/{lab.Id}");

        // then: 204; записей сдач с labId=L.id нет (шов хранилища); GET /labs/{L.id} — 404.
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(submissions.ListByLabIds([lab.Id]));

        using var get = await client.GetAsync($"/api/v1/labs/{lab.Id}");
        await ApiAssert.AssertMessageAsync(get, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
