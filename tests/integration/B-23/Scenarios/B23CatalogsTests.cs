using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B23.Infrastructure;

namespace LabsApp.IntegrationTests.B23.Scenarios;

/// <summary>
/// Справочники и анонимный доступ «текущего пользователя» против реального
/// PostgreSQL: /me/profile от засеянного преподавателя (SQL-сид teacher),
/// /me/submissions без группы, перечень групп с вычисляемым studentCount,
/// CRUD групп/работ с уникальными именами (идемпотентность зоны).
/// Аутентификации в приложении нет (POST /api/v1/auth/* удалён) — клиенты
/// анонимные.
/// </summary>
public sealed class B23CatalogsTests
{
    [Fact]
    public async Task MeProfile_Anonymous_ReturnsSeededTeacherFromSqlSeed()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/profile");

        Assert.Equal("teacher", profile.GetProperty("login").GetString());
        Assert.Equal("teacher", profile.GetProperty("role").GetString());
        Assert.Null(profile.TryGetProperty("groupName", out var group) ? group.GetString() : null);
    }

    [Fact]
    public async Task MeSubmissions_Anonymous_ReturnsEmptyShape()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/submissions?semester=1");

        // Анонимный контракт /me/submissions: без группы, без сдач.
        Assert.False(mine.GetProperty("hasGroup").GetBoolean());
        Assert.Equal(0, mine.GetProperty("labs").GetArrayLength());
        Assert.Equal(0, mine.GetProperty("submissions").GetArrayLength());
    }

    [Fact]
    public async Task Groups_List_SeedCountsComputed()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/groups")
            ?? throw new InvalidOperationException("Перечень групп недоступен.");

        var byName = groups.ToDictionary(g => g.GetProperty("name").GetString()!);
        Assert.Equal(25, byName["ИК-221"].GetProperty("studentCount").GetInt32());
        Assert.Equal(5, byName["ИК-222"].GetProperty("studentCount").GetInt32());
        Assert.Equal(0, byName["ИК-223"].GetProperty("studentCount").GetInt32());
    }

    [Fact]
    public async Task Group_CreateRenameDelete_Lifecycle()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var name = $"Группа-B23-{Guid.NewGuid():N}";
        var renamed = $"{name}-renamed";

        // Создание → 201 GroupDto со studentCount=0.
        var create = await client.PostAsync(
            "/api/v1/groups",
            new StringContent($"{{\"name\":\"{name}\"}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal(0, created.GetProperty("studentCount").GetInt32());

        // Переименование → 200.
        var update = await client.PutAsync(
            $"/api/v1/groups/{id}",
            new StringContent($"{{\"name\":\"{renamed}\"}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // Дубль имени (уникальный для прогона) при создании → 409.
        var duplicate = await client.PostAsync(
            "/api/v1/groups",
            new StringContent($"{{\"name\":\"{renamed}\"}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Удаление → 204; повторное удаление → 404 (зона чистит за собой).
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/v1/groups/{id}")).StatusCode);
    }

    [Fact]
    public async Task Group_Delete_DetachesItsStudents()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        // Группа + перевод в неё студента 32 (изначально без группы).
        var name = $"Группа-B23-detach-{Guid.NewGuid():N}";
        var create = await client.PostAsync(
            "/api/v1/groups",
            new StringContent($"{{\"name\":\"{name}\"}}", Encoding.UTF8, "application/json"));
        var group = await create.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = group.GetProperty("id").GetString()!;

        var studentId = await FindStudentIdAsync(client, "student32");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/v1/students/{studentId}/group",
            new { groupId })).StatusCode);

        // Удаление группы → студент снова без группы (ON DELETE SET NULL).
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/groups/{groupId}")).StatusCode);

        var students = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/students?search=student32");
        Assert.Null(students.GetProperty("items")[0].GetProperty("groupId").GetString());
    }

    private static async Task<string> FindStudentIdAsync(HttpClient client, string login)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/students?search={login}");
        return page.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Labs_CreateDuplicateConflictDelete_Lifecycle()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        // Пара вне сида (семестр 9): создание → 201, дубль пары → 409, удаление → 204.
        var body = $$"""
            {
              "semester": 9,
              "number": 91,
              "content": "Работа B23",
              "assignmentUrl": null,
              "defenseRequired": true
            }
            """;
        var create = await client.PostAsync(
            "/api/v1/labs", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var duplicate = await client.PostAsync(
            "/api/v1/labs", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/v1/labs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/labs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/labs/{id}")).StatusCode);
    }

    [Fact]
    public async Task Labs_SemesterFilter_SeedTwentyLabsFirstSemester()
    {
        using var factory = new B23WebAppFactory();
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/labs?semester=1");

        Assert.Equal(20, page.GetProperty("total").GetInt32());
        var first = page.GetProperty("items")[0];
        Assert.Equal(1, first.GetProperty("semester").GetInt32());
        Assert.Equal(1, first.GetProperty("number").GetInt32());
    }
}
