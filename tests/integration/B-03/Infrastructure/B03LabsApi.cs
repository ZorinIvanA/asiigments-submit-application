namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Запросы кейсов списков работ B-03 (TS-088..TS-091, FR-017) к GET /api/v1/labs и
/// чтение PagedResult&lt;LabDto&gt;. Query передаётся «сырой» строкой: кейсы проверяют
/// мягкую нормализацию нечисловых/дробных значений semester и page (ISS-009/AR-002)
/// — параметры должны дойти до сервера дословно, без клиентской типизации.
/// </summary>
public static class B03LabsApi
{
    /// <summary>GET /api/v1/labs (query — «сырая» строка параметров либо null).</summary>
    public static Task<HttpResponseMessage> GetLabsAsync(HttpClient client, string? query = null) =>
        client.GetAsync(string.IsNullOrEmpty(query) ? "/api/v1/labs" : $"/api/v1/labs?{query}");

    /// <summary>Перечень пар (semester, number) из items PagedResult — в порядке ответа.</summary>
    public static (int Semester, int Number)[] ReadItemPairs(JsonElement pagedBody)
    {
        var items = pagedBody.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        return items
            .EnumerateArray()
            .Select(item => (item.GetProperty("semester").GetInt32(), item.GetProperty("number").GetInt32()))
            .ToArray();
    }

    /// <summary>Значения int-полей PagedResult (page/pageSize/total) из тела ответа.</summary>
    public static int ReadInt(JsonElement pagedBody, string propertyName)
    {
        Assert.True(
            pagedBody.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number,
            $"Ожидалось числовое поле '{propertyName}' в теле списка, фактически: {pagedBody.GetRawText()}");
        return value.GetInt32();
    }
}
