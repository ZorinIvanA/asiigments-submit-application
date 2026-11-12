using System.Text.Json;

namespace LabsApp.IntegrationTests.B06.Infrastructure;

/// <summary>
/// Построители запросов волны сдач B-06 (FR-017/FR-020/FR-021) и чтение тел списков.
/// Значения query подставляются как заданы кейсом (включая нечисловой «abc») —
/// контракт строгих/мягких проверок семестра проверяется на «сырых» строках.
/// </summary>
public static class B06SubmissionsApi
{
    public const string SubmissionsEndpoint = "/api/v1/submissions";
    public const string MeSubmissionsEndpoint = "/api/v1/me/submissions";
    public const string LabsEndpoint = ApiRequests.LabsEndpoint;
    public const string StudentsEndpoint = "/api/v1/students";

    /// <summary>GET /submissions?groupId=&amp;semester=&amp;page= — все параметры сырыми строками.</summary>
    public static Task<HttpResponseMessage> GetGridAsync(
        HttpClient client,
        string? groupId,
        string? semester,
        string? page) =>
        client.GetAsync(BuildQuery(SubmissionsEndpoint, new Dictionary<string, string?>
        {
            ["groupId"] = groupId,
            ["semester"] = semester,
            ["page"] = page,
        }));

    /// <summary>PUT /submissions с телом кейса: null-даты уходят в JSON null (сброс даты).</summary>
    public static Task<HttpResponseMessage> PutSubmissionAsync(
        HttpClient client,
        string studentId,
        string labId,
        string? submitDate,
        string? defenseDate) =>
        client.PutAsJsonAsync(SubmissionsEndpoint, new
        {
            studentId,
            labId,
            submitDate,
            defenseDate,
        });

    /// <summary>GET /me/submissions?semester= — semester сырой строкой (числа, 11, «abc»).</summary>
    public static Task<HttpResponseMessage> GetMeSubmissionsAsync(HttpClient client, string? semester) =>
        client.GetAsync(BuildQuery(MeSubmissionsEndpoint, new Dictionary<string, string?>
        {
            ["semester"] = semester,
        }));

    /// <summary>GET /labs?semester=1 — список работ (id записи для GET /labs/{id}, TS-198).</summary>
    public static Task<HttpResponseMessage> GetLabsBySemesterAsync(HttpClient client, string semester) =>
        client.GetAsync(BuildQuery(LabsEndpoint, new Dictionary<string, string?>
        {
            ["semester"] = semester,
        }));

    /// <summary>GET /labs/{id} — работа по uuid (TS-198).</summary>
    public static Task<HttpResponseMessage> GetLabByIdAsync(HttpClient client, string labId) =>
        client.GetAsync($"{LabsEndpoint}/{Uri.EscapeDataString(labId)}");

    /// <summary>DELETE /labs/{id} — удаление работы (TS-201: несуществующий uuid).</summary>
    public static Task<HttpResponseMessage> DeleteLabByIdAsync(HttpClient client, string labId) =>
        client.DeleteAsync($"{LabsEndpoint}/{Uri.EscapeDataString(labId)}");

    /// <summary>GET /students?page= — страница списка студентов (TS-203).</summary>
    public static Task<HttpResponseMessage> GetStudentsPageAsync(HttpClient client, string page) =>
        client.GetAsync(BuildQuery(StudentsEndpoint, new Dictionary<string, string?>
        {
            ["page"] = page,
        }));

    /// <summary>
    /// Собирает query, пропуская null-значения (кейс TS-145: запрос БЕЗ groupId —
    /// параметр не передаётся вовсе, а не передаётся пустой строкой).
    /// </summary>
    private static string BuildQuery(string endpoint, Dictionary<string, string?> parameters)
    {
        var pairs = parameters
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var query = string.Join("&", pairs);
        return query.Length == 0 ? endpoint : $"{endpoint}?{query}";
    }

    /// <summary>Элемент массива <paramref name="arrayName"/> по индексу (иначе — падение с фактическим телом).</summary>
    public static JsonElement GetArrayElement(JsonElement root, string arrayName, int index)
    {
        Assert.Equal(JsonValueKind.Array, root.GetProperty(arrayName).ValueKind);
        Assert.True(
            root.GetProperty(arrayName).GetArrayLength() > index,
            $"Ожидался элемент {index} в «{arrayName}», фактическая длина: {root.GetProperty(arrayName).GetArrayLength()}.");
        return root.GetProperty(arrayName)[index];
    }
}
