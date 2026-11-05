using System.Text.Json;

namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Читатели транспортных форм списков API, проверяемых кейсами сида (TS-137/TS-138):
/// PagedResult {items, total, page, pageSize} либо «сырой» массив; ведомость
/// SubmissionsGridDto {students, labs, submissions, total, page}. Кейс проверяет
/// СОСТАВ (пустота/количество), а не форму пагинации — читатели допускают обе формы
/// одинаково честно.
/// </summary>
public static class ApiShapes
{
    /// <summary>Полный размер выборки: total у PagedResult, длина — у сырого массива.</summary>
    public static int TotalOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Array
            ? payload.GetArrayLength()
            : payload.GetProperty("total").GetInt32();

    /// <summary>Элементы текущей страницы: items у PagedResult, сам элемент — у массива.</summary>
    public static JsonElement ItemsOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Array ? payload : payload.GetProperty("items");

    /// <summary>
    /// Сдача «реальна»: есть хотя бы одна дата (инвариант хранилища FR-025/FR-022 —
    /// обе null означают отсутствие записи; ведомость может отдавать и пустые пары).
    /// </summary>
    public static bool IsRealSubmission(JsonElement submission) =>
        submission.GetProperty("submitDate").ValueKind == JsonValueKind.String
        || submission.GetProperty("defenseDate").ValueKind == JsonValueKind.String;

    /// <summary>uuid группы по точному имени из ответа списка групп, null — если нет.</summary>
    public static string? FindGroupIdByName(JsonElement groupsPayload, string name)
    {
        foreach (var group in ItemsOf(groupsPayload).EnumerateArray())
        {
            if (string.Equals(group.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            {
                return group.GetProperty("id").GetString();
            }
        }

        return null;
    }
}
