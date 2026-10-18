using System.Text.Json.Serialization;

namespace LabsApp.Hosting;

/// <summary>
/// Единый конверт ошибок /api (IF-001): {message, errors?}.
/// Поле errors присутствует ТОЛЬКО у 400 с полевыми ошибками; нормализованные
/// фреймворковые 400 отдаются без него.
/// </summary>
public sealed class ErrorEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; init; }
}
