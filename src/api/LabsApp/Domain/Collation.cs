using System.Globalization;

namespace LabsApp.Domain;

/// <summary>
/// Единый помощник коллации (ADR-015) — ДВА раздельных строковых правила:
///  1) ci-правило глоссария — <see cref="Key"/>/<see cref="Equals"/>/<see cref="Contains"/>:
///     приведение регистра <see cref="string.ToLowerInvariant"/>, затем порядковое
///     сравнение по кодовым единицам UTF-16 (<see cref="StringComparer.Ordinal"/>).
///     Применяется для уникальностей login/email/названия группы, ключей лимитеров
///     и поиска подстрок. Следствия правила: «Ё» не приравнивается к «Е» (разные
///     кодовые точки после приведения регистра); порядок не зависит от культуры ОС.
///  2) <see cref="SortComparer"/> — сортировки списков (name↑, fullName↑, login↑):
///     правила русской локали без учёта регистра (CompareInfo ru-RU, IgnoreCase —
///     аналог Intl.Collator('ru'), ASM-006). Реворк: прежде сортировки шли по
///     ordinal-ключу — это нарушало FR-040/FR-044/FR-050.
/// Правила НЕ смешиваются: уникальности/ключи/поиск — только ci-правило (1),
/// сортировки списков — только SortComparer (2).
/// </summary>
public static class Collation
{
    /// <summary>CompareInfo русской локали для сортировки списков (кэшируется культурой).</summary>
    private static readonly CompareInfo RuSortCompareInfo = CompareInfo.GetCompareInfo("ru-RU");

    /// <summary>
    /// Ключ ci-сравнения: нижний регистр по инвариантной культуре.
    /// null трактуется как пустая строка.
    /// </summary>
    public static string Key(string? value) => value?.ToLowerInvariant() ?? string.Empty;

    /// <summary>ci-равенство: ключи совпадают посимвольно (ordinal).</summary>
    public static bool Equals(string? left, string? right) =>
        string.Equals(Key(left), Key(right), StringComparison.Ordinal);

    /// <summary>
    /// ci-поиск подстроки (FR-018/FR-019/FR-021): token входит в source
    /// при сравнении ключей и посимвольном поиске вхождения.
    /// </summary>
    public static bool Contains(string? source, string? token)
    {
        return source is not null
            && token is not null
            && Key(source).Contains(Key(token), StringComparison.Ordinal);
    }

    /// <summary>
    /// Сравнение для сортировок списков: русская локаль без учёта регистра
    /// (аналог Intl.Collator('ru'), ASM-006). НЕ применяется к уникальностям,
    /// ключам и поиску — там действует ci-правило <see cref="Key"/> (ordinal).
    /// null ставится раньше непустых строк.
    /// </summary>
    public static int CompareForSort(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        return RuSortCompareInfo.Compare(left, right, CompareOptions.IgnoreCase);
    }

    /// <summary>Сравнитель сортировки списков (name↑, fullName↑, login↑) — для List.Sort/OrderBy.</summary>
    public static IComparer<string?> SortComparer { get; } =
        Comparer<string?>.Create(CompareForSort);
}
