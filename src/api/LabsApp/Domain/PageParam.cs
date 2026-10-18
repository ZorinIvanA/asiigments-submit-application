using System.Globalization;

namespace LabsApp.Domain;

/// <summary>
/// Нормализация параметра page списковых эндпоинтов (глоссарий «Нормализация страницы»):
/// целое ≥ 1 — как есть; отсутствует / не целое / &lt; 1 / переполнение int → 1.
/// Эхо исходного некорректного значения в ответе запрещено — наружу отдаётся только
/// нормализованный номер страницы.
/// </summary>
public static class PageParam
{
    /// <summary>Номер страницы по умолчанию; результат нормализации любого некорректного значения.</summary>
    public const int DefaultPage = 1;

    /// <summary>
    /// Нормализация «сырого» строкового значения запроса: целое ≥ 1 — как есть;
    /// «0», «-1», «abc», «2.5», пустое, переполнение — <see cref="DefaultPage"/>.
    /// </summary>
    public static int Normalize(string? rawPage) =>
        int.TryParse(rawPage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
            && page >= DefaultPage
            ? page
            : DefaultPage;

    /// <summary>Нормализация уже числового значения: page &lt; 1 → <see cref="DefaultPage"/>.</summary>
    public static int Normalize(int page) =>
        page >= DefaultPage ? page : DefaultPage;
}
