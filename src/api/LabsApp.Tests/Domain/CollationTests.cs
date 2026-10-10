using LabsApp.Domain;

namespace LabsApp.Tests.Domain;

/// <summary>
/// Юнит-проверки коллации (ADR-015, T-002). ДВА правила не смешиваются:
///  1) ci-ключ Key/Equals/Contains = ToLowerInvariant + ordinal по кодовым единицам
///     UTF-16 (уникальности/ключи/поиск): Ё ≠ Е, порядок не зависит от культуры;
///  2) SortComparer = русская локаль без учёта регистра (аналог Intl.Collator('ru'),
///     ASM-006) — сортировки списков: [«Б», «а», «В»] → [«а», «Б», «В»].
/// </summary>
public sealed class CollationTests
{
    // ------------------------------------------------------------------
    // Правило 1: ci-ключ (lower-invariant + ordinal) — не изменился реворком
    // ------------------------------------------------------------------

    [Fact]
    public void Key_LowercasesByInvariant()
    {
        Assert.Equal("иванов", Collation.Key("Иванов"));
        Assert.Equal("иванов", Collation.Key("ИВАНОВ"));
        Assert.Equal("ёлка", Collation.Key("Ёлка"));
    }

    [Fact]
    public void Key_Null_IsEmptyString()
    {
        Assert.Equal(string.Empty, Collation.Key(null));
    }

    [Fact]
    public void Key_RemainsLowerInvariantOrdinal_RulesNotMixed()
    {
        // AC SortComparer: «ci-ключ Key при этом по-прежнему lower-invariant ordinal».
        Assert.Equal("ёлка", Collation.Key("Ёлка"));
        // «е» U+0435 < «ё» U+0451 — порядок по кодовым единицам, не по русской локали.
        Assert.True(string.CompareOrdinal(Collation.Key("Елка"), Collation.Key("Ёлка")) < 0);
        Assert.False(Collation.Equals("Ёлка", "Елка"));
    }

    [Fact]
    public void Equals_CaseInsensitive_SameLetters()
    {
        Assert.True(Collation.Equals("иванов", "Иванов"));
        Assert.True(Collation.Equals("Student01", "student01"));
        Assert.True(Collation.Equals("СТУДЕНТ", "студент"));
    }

    [Fact]
    public void Equals_YoIsNotE()
    {
        // Критерий коллации: «Ёлка» и «Елка» — разные ключи (Ё ≠ Е).
        Assert.False(Collation.Equals("Ёлка", "Елка"));
        Assert.False(Collation.Equals("Ёлкин", "Елкин"));
    }

    [Fact]
    public void Equals_IsInvariantToOsCulture_TurkishI()
    {
        // Инвариантное приведение регистра: 'I' → 'i' (в tr-TR было бы 'ı').
        Assert.True(Collation.Equals("Istanbul", "istanbul"));
        Assert.Equal("istanbul", Collation.Key("ISTANBUL"));
    }

    [Theory]
    [InlineData("Иванов Иван", "иван", true)]
    [InlineData("Иванов Иван", "ВАН", true)]
    [InlineData("student01@example.com", "STUDENT01", true)]
    [InlineData("Ёлкин", "ёлк", true)]
    [InlineData("Ёлкин", "елк", false)]
    [InlineData("Иванов", "пётр", false)]
    public void Contains_IsCaseInsensitiveOrdinalSubstring(string source, string token, bool expected)
    {
        Assert.Equal(expected, Collation.Contains(source, token));
    }

    [Fact]
    public void Contains_EmptyToken_MirrorsStringContains()
    {
        // Пустой токен — вхождение как у string.Contains; токены поиска FR-019 после
        // сплита по пробелам пустыми не бывают (пустой search = без фильтра).
        Assert.True(Collation.Contains("Иванов", ""));
    }

    [Fact]
    public void Contains_NullArguments_ReturnsFalse()
    {
        Assert.False(Collation.Contains(null, "иван"));
        Assert.False(Collation.Contains("Иванов", null));
    }

    // ------------------------------------------------------------------
    // Правило 2: SortComparer — русская локаль без учёта регистра
    // ------------------------------------------------------------------

    [Fact]
    public void SortComparer_MixedCaseCyrillic_OrdersAlphabetically()
    {
        // Критерий AC: [«Б», «а», «В»] → [«а», «Б», «В»] — русская локаль без учёта
        // регистра (аналог Intl.Collator('ru'), ASM-006).
        var sorted = new List<string?> { "Б", "а", "В" };
        sorted.Sort(Collation.SortComparer);

        Assert.Equal(new[] { "а", "Б", "В" }, sorted);
    }

    [Fact]
    public void SortComparer_MixedCaseCyrillicFullNames_IgnoreCase()
    {
        var sorted = new[] { "яковлев", "Абрамов", "Ёлкин", "борисов" }
            .OrderBy(name => name, Collation.SortComparer)
            .ToArray();

        // Регистр игнорируется: порядок по алфавиту независимо от регистра написания.
        Assert.Equal(new[] { "Абрамов", "борисов", "Ёлкин", "яковлев" }, sorted);
    }

    [Fact]
    public void SortComparer_CaseEquivalentStrings_CompareEqual()
    {
        // Эквивалентные по регистру строки сравниваются как равные (не смешиваются
        // с ordinal-ключом, где «Иванов» ≠ «иванов» по кодовым точкам регистра).
        Assert.Equal(0, Collation.CompareForSort("иванов", "Иванов"));
        Assert.Equal(0, Collation.CompareForSort("ИВАНОВ", "иванов"));
        Assert.Equal(0, Collation.CompareForSort("STUDENT01", "student01"));
        Assert.Equal(0, Collation.CompareForSort("ИК-221", "ик-221"));
    }

    [Fact]
    public void SortComparer_LatinAndCyrillic_GroupByScript_CaseIgnored()
    {
        // Латиница+кириллица: правило русской локали ставит базовый алфавит локали
        // (кириллицу) раньше латиницы, регистр внутри алфавита игнорируется
        // («а» и «А» эквивалентны, «z» и «a» упорядочены по алфавиту латиницы).
        var sorted = new[] { "я", "z", "А", "a" }
            .OrderBy(name => name, Collation.SortComparer)
            .ToArray();

        Assert.Equal(new[] { "А", "я", "a", "z" }, sorted);
    }

    [Fact]
    public void SortComparer_NullSortsFirst()
    {
        Assert.True(Collation.CompareForSort(null, "иванов") < 0);
        Assert.True(Collation.CompareForSort("иванов", null) > 0);
        Assert.Equal(0, Collation.CompareForSort(null, null));
    }

    [Fact]
    public void SortComparer_SameReference_IsZero()
    {
        Assert.Equal(0, Collation.CompareForSort("иванов", "иванов"));
    }
}
