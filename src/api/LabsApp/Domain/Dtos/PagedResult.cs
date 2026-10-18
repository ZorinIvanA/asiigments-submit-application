using System.Text.Json.Serialization;

namespace LabsApp.Domain.Dtos;

/// <summary>
/// Страница списка с лабораторной пагинацией (IF-LABS/IF-GROUPS/IF-STUDENTS):
/// {items, total, page, pageSize}. Транспортная форма дословно повторяет
/// PagedResult клиента (ASM-005). Нормализация page — <see cref="Normalize"/>
/// (делегирует <see cref="PageParam.Normalize"/>): некорректное исходное значение
/// никогда не эхо-возвращается (глоссарий «Нормализация страницы»).
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>Номер страницы по умолчанию (page &lt; 1, нечисловая строка и пр.).</summary>
    public const int DefaultPage = PageParam.DefaultPage;

    /// <summary>Элементы текущей страницы (выборка упорядочена до нарезки).</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Размер полной выборки после фильтра/сортировки.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>НОРМАЛИЗОВАННЫЙ номер страницы (1-based).</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; }

    /// <summary>Размер страницы (10 для списков, 5 для ведомости — FR-013/FR-016).</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    /// <summary>
    /// Нормализация «сырого» строкового значения запроса — <see cref="PageParam.Normalize(string?)"/>.
    /// </summary>
    public static int Normalize(string? rawPage) => PageParam.Normalize(rawPage);

    /// <summary>Нормализация числового значения — <see cref="PageParam.Normalize(int)"/>.</summary>
    public static int Normalize(int page) => PageParam.Normalize(page);
}
