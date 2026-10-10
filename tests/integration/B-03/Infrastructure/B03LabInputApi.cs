namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Запросы создания лабораторной работы кейсов B-03 (TS-092..TS-094, FR-017):
/// POST /api/v1/labs. Тело уходит на сервер «как есть» — number строкой из цифр,
/// content с краевыми пробелами, assignmentUrl ''/null: серверная нормализация
/// ПОСЛЕ валидации (FR-017) проверяется по полям ответа. Чтение полей LabDto —
/// с диагностикой фактического тела в сообщениях assert'ов.
/// </summary>
public static class B03LabInputApi
{
    /// <summary>Эндпойнт создания работы (FR-017).</summary>
    public const string Endpoint = "/api/v1/labs";

    /// <summary>POST /api/v1/labs с телом-объектом (null-поля сериализуются литералом null).</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Endpoint, body);

    /// <summary>POST /api/v1/labs с заданным «сырым» JSON-телом.</summary>
    public static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json) =>
        client.PostAsync(Endpoint, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>Строковое поле LabDto (id/content/assignmentUrl); JSON null → null.</summary>
    public static string? ReadString(JsonElement labDto, string propertyName)
    {
        Assert.True(
            labDto.TryGetProperty(propertyName, out var value),
            $"Ожидалось поле '{propertyName}' в теле LabDto, фактически: {labDto.GetRawText()}");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    /// <summary>Числовое поле LabDto (number/semester) — обязано быть JSON-числом.</summary>
    public static int ReadInt(JsonElement labDto, string propertyName)
    {
        Assert.True(
            labDto.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number,
            $"Ожидалось числовое поле '{propertyName}' в теле LabDto, фактически: {labDto.GetRawText()}");
        return value.GetInt32();
    }

    /// <summary>boolean-поле LabDto (defenseRequired) — обязано быть литералом true/false.</summary>
    public static bool ReadBool(JsonElement labDto, string propertyName)
    {
        Assert.True(
            labDto.TryGetProperty(propertyName, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            $"Ожидалось boolean-поле '{propertyName}' в теле LabDto, фактически: {labDto.GetRawText()}");
        return value.ValueKind == JsonValueKind.True;
    }
}
