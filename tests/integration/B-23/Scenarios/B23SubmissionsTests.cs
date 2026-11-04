using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B23.Infrastructure;

namespace LabsApp.IntegrationTests.B23.Scenarios;

/// <summary>
/// Сдачи против реального PostgreSQL (репозиторий сдач — сырой Npgsql:
/// upsert ON CONFLICT + чтения через reader). Изменения затрагивают только
/// пару student01 × работа (1,5), у которой нет сид-отметок, и сбрасываются
/// в null — исходное состояние сохраняется (идемпотентность зоны). Сид-сдача
/// student01 × работа (1,1) проверяется по ведомости (анонимный клиент —
/// аутентификации в приложении нет).
/// </summary>
public sealed class B23SubmissionsTests
{
    [Fact]
    public async Task Upsert_SetDatesThenReset_GridReflectsBothStates()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var (studentId, labId, groupId) = await ResolveGridIdsAsync(client);

        // upsert с датами → 200 полная запись.
        var put = await client.PutAsync(
            "/api/v1/submissions",
            new StringContent(
                $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":"2026-09-05","defenseDate":"2026-09-15"}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-09-05", saved.GetProperty("submitDate").GetString());
        Assert.Equal("2026-09-15", saved.GetProperty("defenseDate").GetString());

        // Ведомость показывает проставленные даты.
        var cell = await FindGridCellAsync(client, groupId, studentId, labId);
        Assert.Equal("2026-09-05", cell.GetProperty("submitDate").GetString());

        // Сброс: обе даты null — запись пары СОХРАНЯЕТСЯ (data_design FR-021).
        var reset = await client.PutAsync(
            "/api/v1/submissions",
            new StringContent(
                $$"""{"studentId":"{{studentId}}","labId":"{{labId}}","submitDate":null,"defenseDate":null}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var cellAfterReset = await FindGridCellAsync(client, groupId, studentId, labId);
        Assert.Null(cellAfterReset.GetProperty("submitDate").GetString());
        Assert.Null(cellAfterReset.GetProperty("defenseDate").GetString());
    }

    [Fact]
    public async Task Grid_SeedSubmissionOfStudent01_SeedDatesVisible()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        // Сид-сдача student01 × работа (1,1) из 03_seed_demo.sql видна в
        // ведомости ИК-221 анонимному клиенту: сдача 2026-09-01, защита 2026-09-11.
        var (studentId, firstLabId, groupId) = await ResolveSeedCellIdsAsync(client);
        var submission = await FindGridCellAsync(client, groupId, studentId, firstLabId);

        Assert.Equal("2026-09-01", submission.GetProperty("submitDate").GetString());
        Assert.Equal("2026-09-11", submission.GetProperty("defenseDate").GetString());
    }

    /// <summary>Id студента student01, работы (1,5) и группы ИК-221.</summary>
    private static async Task<(string StudentId, string LabId, string GroupId)> ResolveGridIdsAsync(
        HttpClient client)
    {
        var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/groups")
            ?? throw new InvalidOperationException("Перечень групп недоступен.");
        var groupId = groups.Single(g => g.GetProperty("name").GetString() == "ИК-221")
            .GetProperty("id").GetString()!;

        var students = await client.GetFromJsonAsync<JsonElement>("/api/v1/students?search=student01");
        var studentId = students.GetProperty("items")[0].GetProperty("id").GetString()!;

        var labs = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs?semester=1");
        var labId = labs.GetProperty("items").EnumerateArray()
            .Single(l => l.GetProperty("number").GetInt32() == 5)
            .GetProperty("id").GetString()!;

        return (studentId, labId, groupId);
    }

    /// <summary>Id студента student01, работы (1,1) и группы ИК-221 (сид-сдача).</summary>
    private static async Task<(string StudentId, string LabId, string GroupId)> ResolveSeedCellIdsAsync(
        HttpClient client)
    {
        var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/groups")
            ?? throw new InvalidOperationException("Перечень групп недоступен.");
        var groupId = groups.Single(g => g.GetProperty("name").GetString() == "ИК-221")
            .GetProperty("id").GetString()!;

        var students = await client.GetFromJsonAsync<JsonElement>("/api/v1/students?search=student01");
        var studentId = students.GetProperty("items")[0].GetProperty("id").GetString()!;

        var labs = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs?semester=1");
        var labId = labs.GetProperty("items").EnumerateArray()
            .Single(l => l.GetProperty("number").GetInt32() == 1)
            .GetProperty("id").GetString()!;

        return (studentId, labId, groupId);
    }

    private static async Task<JsonElement> FindGridCellAsync(
        HttpClient client, string groupId, string studentId, string labId)
    {
        var grid = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/submissions?groupId={groupId}&semester=1&page=1");
        return grid.GetProperty("submissions").EnumerateArray()
            .Single(s => s.GetProperty("studentId").GetString() == studentId
                && s.GetProperty("labId").GetString() == labId);
    }
}
