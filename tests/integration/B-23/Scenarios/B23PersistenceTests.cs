using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LabsApp.IntegrationTests.B23.Infrastructure;

namespace LabsApp.IntegrationTests.B23.Scenarios;

/// <summary>
/// Ключевое отличие реальной БД от заглушек: данные переживают перезапуск
/// процесса. Первый хост создаёт уникальную группу, хост уничтожается
/// (Dispose = «перезапуск»), второй хост обязан увидеть группу в перечне.
/// Группа удаляется в конце — зона идемпотентна. Клиенты анонимные
/// (аутентификации в приложении нет).
/// </summary>
public sealed class B23PersistenceTests
{
    [Fact]
    public async Task CreatedGroup_SurvivesHostRestart()
    {
        var name = $"Группа-B23-persist-{Guid.NewGuid():N}";

        string groupId;
        using (var first = new B23WebAppFactory())
        {
            using var client = first.CreateClient();

            var create = await client.PostAsync(
                "/api/v1/groups",
                new StringContent($"{{\"name\":\"{name}\"}}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            groupId = (await create.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetString()!;
        }

        // «Перезапуск»: новый хост на той же БД.
        using (var second = new B23WebAppFactory())
        {
            using var client = second.CreateClient();

            var groups = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/groups")
                ?? throw new InvalidOperationException("Перечень групп недоступен.");
            Assert.Contains(groups, g => g.GetProperty("name").GetString() == name);

            // Чистим за собой: повторный прогон зоны не увидит остатков.
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/v1/groups/{groupId}")).StatusCode);
        }
    }
}
