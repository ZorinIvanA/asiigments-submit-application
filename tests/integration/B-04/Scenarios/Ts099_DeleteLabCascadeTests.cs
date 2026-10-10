using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B04.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Scenarios;

/// <summary>
/// TS-099 (P0, data_integrity; FR-017 AC «Удаление каскадно» + «несуществующий id → 404»,
/// FR-021) «Лабораторные: удаление — каскад сдач и 404 на несуществующий id».
/// given: есть сдача по работе L (создана PUT /submissions); известен несуществующий
///        uuid; сессия teacher; шов хранилища сдач доступен.
/// when:  DELETE /labs/{L.id}; затем GET /labs/{L.id}; затем DELETE /labs/&lt;несуществующий-uuid&gt;.
/// then:  204; записей сдач с labId=L.id нет (каскадное удаление Submission); GET — 404;
///        повторный DELETE с несуществующим id — 404 'Лабораторная не найдена'
///        (не 204 и не 500).
/// </summary>
public sealed class Ts099_DeleteLabCascadeTests(B04LabsWebAppFactory factory) : IClassFixture<B04LabsWebAppFactory>
{
    private readonly B04LabsWebAppFactory _factory = factory;

    [Fact]
    public async Task TS099_DeleteLab_CascadesSubmissions_UnknownIdReturns404()
    {
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var labs = _factory.Services.GetRequiredService<ILabRepository>();
        var submissions = _factory.Services.GetRequiredService<ISubmissionRepository>();
        var teacher = users.GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given кейса неисполним.");
        var student = B04DomainSeed.AddStudent(users, "b04-ts099-student");
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

        // when/then: DELETE /labs/{L.id} — 204; записей сдач с labId=L.id нет (каскад).
        using var delete = await client.DeleteAsync($"/api/v1/labs/{lab.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(submissions.ListByLabIds([lab.Id]));

        // then: GET /labs/{L.id} — 404.
        using var get = await client.GetAsync($"/api/v1/labs/{lab.Id}");
        await ApiAssert.AssertMessageAsync(get, HttpStatusCode.NotFound, "Лабораторная не найдена");

        // when/then: DELETE по несуществующему uuid — 404 'Лабораторная не найдена'
        // (не 204 и не 500).
        using var deleteUnknown = await client.DeleteAsync($"/api/v1/labs/{Guid.NewGuid()}");
        await ApiAssert.AssertMessageAsync(deleteUnknown, HttpStatusCode.NotFound, "Лабораторная не найдена");
    }
}
