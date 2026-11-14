using System.Text.Json;

namespace LabsApp.IntegrationTests.B08.Groups.Infrastructure;

/// <summary>
/// Читаемые помощники над эндпоинтами групп для сценариев батча: поиск uuid
/// группы по имени в выдаче GET /api/v1/groups и запрос состава группы.
/// Все обращения — через публичный HTTP-контракт (черноящичность зоны).
/// </summary>
public static class B08GroupsApi
{
    /// <summary>uuid группы с именем name из выдачи GET /api/v1/groups.</summary>
    public static async Task<string> GetGroupIdByNameAsync(B08GroupsClient client, string name)
    {
        using var response = await client.GetAsync(B08GroupsClient.GroupsEndpoint);
        using var body = await B08GroupsClient.ReadJsonArrayAsync(
            response, HttpStatusCode.OK, $"GET /api/v1/groups (поиск группы «{name}»)");

        foreach (var group in body.RootElement.EnumerateArray())
        {
            if (B08GroupsClient.StringProperty(group, "name") == name)
            {
                return B08GroupsClient.StringProperty(group, "id");
            }
        }

        throw new Xunit.Sdk.XunitException($"Группа «{name}» не найдена в выдаче GET /api/v1/groups.");
    }

    /// <summary>GET /api/v1/groups/{groupId}/students с сырым значением page (или без него).</summary>
    public static Task<HttpResponseMessage> GetRosterAsync(B08GroupsClient client, string groupId, string? page = null) =>
        client.GetAsync(page is null
            ? $"{B08GroupsClient.GroupsEndpoint}/{groupId}/students"
            : $"{B08GroupsClient.GroupsEndpoint}/{groupId}/students?page={page}");
}
