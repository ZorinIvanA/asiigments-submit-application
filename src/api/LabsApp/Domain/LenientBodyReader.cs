using System.Text;
using System.Text.Json;

namespace LabsApp.Domain;

/// <summary>
/// Одно из трёх состояний поля lenient-тела (ADR-014).
/// </summary>
public enum LenientFieldKind
{
    /// <summary>Поле — JSON-строка; <see cref="LenientField.Value"/> — значение без трима.</summary>
    String,

    /// <summary>Поле — литерал null.</summary>
    JsonNull,

    /// <summary>
    /// Поле отсутствует, его значение нестрокового типа (число/bool/объект/массив)
    /// либо тело не разобрано как JSON-объект (битый JSON, пустое тело — все поля Invalid).
    /// </summary>
    Invalid,
}

/// <summary>
/// Три-стейтное значение поля lenient-тела (ADR-014). Состояния
/// String/JsonNull/Invalid НЕ схлопываются: PUT /students/{id}/group обязан различать
/// литерал null (исключение из группы, 204) и мусор/отсутствие (404 «Группа не найдена»).
/// </summary>
public readonly struct LenientField
{
    private LenientField(LenientFieldKind kind, string? value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Состояние поля.</summary>
    public LenientFieldKind Kind { get; }

    /// <summary>Значение JSON-строки БЕЗ трима; для JsonNull/Invalid — null.</summary>
    public string? Value { get; }

    /// <summary>Поле — литерал null.</summary>
    public static LenientField JsonNull { get; } = new(LenientFieldKind.JsonNull, null);

    /// <summary>Поле отсутствует / нестроковое значение / тело не разобрано.</summary>
    public static LenientField Invalid { get; } = new(LenientFieldKind.Invalid, null);

    /// <summary>Поле — JSON-строка со значением без трима.</summary>
    public static LenientField String(string value) => new(LenientFieldKind.String, value);
}

/// <summary>
/// Lenient-чтение JSON-тела запроса (ADR-014) для auth-семейства
/// (register, login, refresh, recovery/request, recovery/confirm, reset-password)
/// и PUT /students/{id}/group: 400 от model binding на этом семействе не возникает.
/// Каждое поле даётся в трёх состояниях (<see cref="LenientField"/>); исключение
/// парсинга поглощается ридером — при битом/пустом/не-объектном теле ВСЕ поля Invalid.
/// Дополнительно ридер различает «битый JSON» и «валидный JSON» флагом
/// <see cref="ParseSuccess"/> (ADR-014): login при битом JSON отвечает 400 (Δkdf=0),
/// при валидном JSON с отсутствующими/нестроковыми полями — 401 после 1 KDF.
/// Проекции выбираются вызовом осознанно: <see cref="GetStringOrNull"/> — только
/// auth-семейство (JsonNull|Invalid → null); полная <see cref="GetField"/> —
/// PUT /students/{id}/group (различает null и мусор, FR-020).
/// </summary>
public sealed class LenientBodyReader
{
    private static readonly IReadOnlyDictionary<string, LenientField> NoFields =
        new Dictionary<string, LenientField>();

    private readonly IReadOnlyDictionary<string, LenientField> _fields;

    /// <summary>
    /// Признак успешного синтаксического разбора JSON: true — <see cref="JsonDocument.Parse"/>
    /// прошёл без исключения (включая корень-не-объект: строка/число/bool/null/массив —
    /// валидный JSON без полей); false — тело пустое либо синтаксически некорректно
    /// (битый JSON). Семантическая граница FR-007/FR-023: битый JSON → 400, валидный
    /// JSON с отсутствующими полями → обычная обработка (login — 401 после 1 KDF).
    /// </summary>
    public bool ParseSuccess { get; }

    /// <summary>
    /// Разбирает тело, прочитанное как текст UTF-8. Битый JSON, пустое тело и корень
    /// не-объект (строка/число/bool/null/массив) дают ридер без полей (все Invalid);
    /// исключение парсинга наружу не покидает. <see cref="ParseSuccess"/> = true
    /// только при успешном разборе (для не-объектного корня — тоже true, полей нет).
    /// </summary>
    public LenientBodyReader(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            _fields = NoFields;
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            ParseSuccess = true;
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _fields = NoFields;
                return;
            }

            var fields = new Dictionary<string, LenientField>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                fields[property.Name] = ToField(property.Value);
            }

            _fields = fields;
        }
        catch (JsonException)
        {
            // Битый JSON: поглощаем исключение парсинга — все поля Invalid,
            // ParseSuccess остаётся false (ADR-014).
            _fields = NoFields;
        }
    }

    /// <summary>
    /// ПОЛНАЯ проекция (три состояния): String — JSON-строка без трима,
    /// JsonNull — литерал null, Invalid — отсутствие/нестрока/битое тело.
    /// </summary>
    public LenientField GetField(string fieldName) =>
        _fields.TryGetValue(fieldName, out var field) ? field : LenientField.Invalid;

    /// <summary>
    /// Проекция auth-семейства (FR-013/FR-017): String → значение, JsonNull|Invalid → null.
    /// Семантика auth-эндпоинтов не различает null и мусор — collaps допустим только здесь.
    /// </summary>
    public string? GetStringOrNull(string fieldName) =>
        GetField(fieldName) is { Kind: LenientFieldKind.String } field ? field.Value : null;

    /// <summary>Читает поток тела (например, HttpRequest.Body) как UTF-8 и строит ридер.</summary>
    public static async Task<LenientBodyReader> FromStreamAsync(
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var reader = new StreamReader(body, Encoding.UTF8);
        return new LenientBodyReader(await reader.ReadToEndAsync(cancellationToken));
    }

    private static LenientField ToField(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => LenientField.String(value.GetString()!),
        JsonValueKind.Null => LenientField.JsonNull,
        _ => LenientField.Invalid,
    };
}
