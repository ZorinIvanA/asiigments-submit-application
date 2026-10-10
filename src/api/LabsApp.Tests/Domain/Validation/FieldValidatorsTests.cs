using LabsApp.Domain.Validation;

namespace LabsApp.Tests.Domain.Validation;

/// <summary>
/// Юнит-проверки полевых валидаторов (T-002, границы глоссария v2.2): границы длин
/// 100/101, 200/201, 254/255, 500/501, 1000/1001, поиск 200/201; пароль 7/8 и
/// 128/129 (&gt;128 — только password.max; пустая строка — все 4 правила сразу);
/// даты 2026-02-30, 01.10.2026, валидная; трим и накопление нарушенных правил.
/// </summary>
public sealed class FieldValidatorsTests
{
    /// <summary>Ровно этот перечень текстов и ничего больше.</summary>
    private static void AssertErrors(IReadOnlyList<string> actual, params string[] expected)
    {
        Assert.Equal(expected, actual);
    }

    // ------------------------------------------------------------------
    // Логин: 1–100 после трима, [A-Za-z0-9._-]
    // ------------------------------------------------------------------

    [Fact]
    public void Login_Boundary100_Valid_101_Invalid()
    {
        AssertErrors(FieldValidators.Login(new string('a', 100)));
        AssertErrors(
            FieldValidators.Login(new string('a', 101)),
            ErrorTexts.LoginLength);
    }

    [Fact]
    public void Login_Boundary1Character_Valid()
    {
        // Нижняя граница FR-006: ровно 1 символ после трима — валиден.
        AssertErrors(FieldValidators.Login("a"));
    }

    [Fact]
    public void Login_Charset_CyrillicInvalid()
    {
        AssertErrors(FieldValidators.Login("иван"), ErrorTexts.LoginCharset);
    }

    [Fact]
    public void Login_AllowedCharset_Valid()
    {
        AssertErrors(FieldValidators.Login("Student_01.x-9"));
    }

    [Fact]
    public void Login_Trimmed_Empty_IsRequired()
    {
        AssertErrors(FieldValidators.Login(null), ErrorTexts.Required);
        AssertErrors(FieldValidators.Login("   "), ErrorTexts.Required);
    }

    [Fact]
    public void Login_Trimmed_ValueIsValidatedAfterTrim()
    {
        AssertErrors(FieldValidators.Login("  student01  "));
    }

    [Fact]
    public void Login_BothViolations_AreReportedTogether()
    {
        AssertErrors(
            FieldValidators.Login(new string('и', 101)),
            ErrorTexts.LoginLength,
            ErrorTexts.LoginCharset);
    }

    // ------------------------------------------------------------------
    // Email: формат + ≤254
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("a@b.ru")]
    [InlineData("ivanov.i.i@example.com")]
    [InlineData("a+b@sub.domain.org")]
    public void Email_ValidFormats_Valid(string email)
    {
        AssertErrors(FieldValidators.Email(email));
    }

    [Theory]
    [InlineData("a@b")]
    [InlineData("a@b..ru")]
    [InlineData("a@b.ru.")]
    [InlineData("a b@c.ru")]
    [InlineData("user@@example.com")]
    [InlineData("userexample.com")]
    public void Email_InvalidFormats_Invalid(string email)
    {
        AssertErrors(FieldValidators.Email(email), ErrorTexts.EmailFormat);
    }

    [Fact]
    public void Email_Boundary254_Valid_255_Invalid()
    {
        var valid = new string('a', 249) + "@b.ru"; // 254 символа
        var invalid = new string('a', 250) + "@b.ru"; // 255 символов

        Assert.Equal(FieldValidators.EmailMaxLength, valid.Length);
        AssertErrors(FieldValidators.Email(valid));
        AssertErrors(FieldValidators.Email(invalid), ErrorTexts.EmailLength);
    }

    [Fact]
    public void Email_Trimmed_BoundaryMeasuredAfterTrim()
    {
        var email = "  " + new string('a', 249) + "@b.ru  ";

        AssertErrors(FieldValidators.Email(email));
    }

    [Fact]
    public void Email_Empty_IsRequired()
    {
        AssertErrors(FieldValidators.Email(""), ErrorTexts.Required);
        AssertErrors(FieldValidators.Email(null), ErrorTexts.Required);
    }

    [Fact]
    public void Email_FormatCheckedBeforeLength()
    {
        var malformed = new string('a', 300) + "@b";

        AssertErrors(FieldValidators.Email(malformed), ErrorTexts.EmailFormat);
    }

    // ------------------------------------------------------------------
    // Пароль (FR-006): сырая строка БЕЗ трима, 8–128; <8 — ВСЕ сработавшие
    // правила сразу; >128 — только password.max
    // ------------------------------------------------------------------

    [Fact]
    public void Password_Boundary7_Invalid_8_Valid()
    {
        // 7 символов с цифрой, буквой и спецзнаком — нарушена только нижняя граница.
        AssertErrors(FieldValidators.Password("abcde6!"), ErrorTexts.PasswordMin);
        AssertErrors(FieldValidators.Password("abcd678!"));
    }

    [Fact]
    public void Password_Exactly128_WithAllCharacterClasses_Valid()
    {
        var max = new string('a', 126) + "1!";

        Assert.Equal(FieldValidators.PasswordMaxLength, max.Length);
        AssertErrors(FieldValidators.Password(max));
    }

    [Fact]
    public void Password_Exactly127_WithAllCharacterClasses_Valid()
    {
        // Граница «127/128/129»: 127 — внутри диапазона 8–128, валиден.
        var inside = new string('a', 125) + "1!";

        Assert.Equal(127, inside.Length);
        AssertErrors(FieldValidators.Password(inside));
    }

    [Fact]
    public void Password_Exactly129_OnlyPasswordMax()
    {
        var over = new string('a', 127) + "1!";

        AssertErrors(FieldValidators.Password(over), ErrorTexts.PasswordMax);
    }

    [Fact]
    public void Password_OverMax_CharacterRulesNotEvaluated()
    {
        // FR-006: при длине >128 проверяется ТОЛЬКО password.max — состав символов
        // игнорируется (какой бы «слабый» ни был хвост: только буквы / только цифры).
        AssertErrors(FieldValidators.Password(new string('a', 129)), ErrorTexts.PasswordMax);
        AssertErrors(FieldValidators.Password(new string('9', 200)), ErrorTexts.PasswordMax);
    }

    [Fact]
    public void Password_ShortWeak_ABC_ReportsMinDigitSpecial()
    {
        // FR-006 AC: password 'abc' — ровно 3 текста (min + digit + special);
        // буква есть — password.letter не выдаётся.
        AssertErrors(
            FieldValidators.Password("abc"),
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);
    }

    [Fact]
    public void Password_Empty_AllFourRulesReportedAtOnce()
    {
        // Пустая строка (и null: нестроковое поле JSON → пустая строка, FR-006) —
        // длина 0 <8: нарушены ВСЕ четыре правила одновременно.
        var expected = new[]
        {
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordLetter,
            ErrorTexts.PasswordSpecial,
        };

        AssertErrors(FieldValidators.Password(""), expected);
        AssertErrors(FieldValidators.Password(null), expected);
    }

    [Fact]
    public void Password_WhitespaceOnly_MinPlusMissingClasses()
    {
        // «  » — 2 спецзнака: спецзнак есть, поэтому min + digit + letter (без трима).
        AssertErrors(
            FieldValidators.Password("  "),
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordLetter);
    }

    [Fact]
    public void Password_MissingDigitAndSpecial_ReportedTogether()
    {
        AssertErrors(
            FieldValidators.Password("abcdefgh"),
            ErrorTexts.PasswordDigit,
            ErrorTexts.PasswordSpecial);
    }

    [Fact]
    public void Password_MissingLetter_Invalid()
    {
        AssertErrors(FieldValidators.Password("1234567!"), ErrorTexts.PasswordLetter);
    }

    [Fact]
    public void Password_MissingDigit_Invalid()
    {
        AssertErrors(FieldValidators.Password("abcdefg!"), ErrorTexts.PasswordDigit);
    }

    [Fact]
    public void Password_Length7WithoutDigit_ReportsMinAndDigit()
    {
        // Критерий T-002 «Границы пароля»: 7 символов (буквы + спецзнак, без цифры)
        // — ровно [min, digit]; правило special не сработало (пробел-спецзнак есть).
        AssertErrors(
            FieldValidators.Password("abcdef!"),
            ErrorTexts.PasswordMin,
            ErrorTexts.PasswordDigit);
    }

    [Fact]
    public void Password_MissingSpecial_Invalid()
    {
        AssertErrors(FieldValidators.Password("abcdefg8"), ErrorTexts.PasswordSpecial);
    }

    [Fact]
    public void Password_IsNotTrimmed_WhitespaceIsPartOfPassword()
    {
        // « abcd678» — 8 символов с ведущим пробелом (пробел = спецзнак): валиден.
        // Будь значение тримнуто, осталось бы 7 символов и password.min.
        AssertErrors(FieldValidators.Password(" abcd678"));
    }

    [Fact]
    public void Password_CyrillicLetterCounts()
    {
        AssertErrors(FieldValidators.Password("абвгде6!"));
    }

    // ------------------------------------------------------------------
    // Совпадение пароля и повтора
    // ------------------------------------------------------------------

    [Fact]
    public void PasswordMatch_EqualOrdinal_Valid()
    {
        AssertErrors(FieldValidators.PasswordMatch(" abcd678!", " abcd678!"));
    }

    [Fact]
    public void PasswordMatch_Mismatch_Invalid()
    {
        AssertErrors(
            FieldValidators.PasswordMatch("abcd678!", "другое"),
            ErrorTexts.PasswordMismatch);
    }

    [Fact]
    public void PasswordMatch_EmptyRepeat_SkippedHere()
    {
        // Пустой повтор — зона required; mismatch здесь не возвращается.
        AssertErrors(FieldValidators.PasswordMatch("abcd678!", ""));
        AssertErrors(FieldValidators.PasswordMatch("", "abcd678!"));
    }

    // ------------------------------------------------------------------
    // ФИО 200/201, название группы 100/101, содержание 500/501
    // ------------------------------------------------------------------

    [Fact]
    public void FullName_Boundary200_Valid_201_Invalid()
    {
        AssertErrors(FieldValidators.FullName(new string('ф', 200)));
        AssertErrors(
            FieldValidators.FullName(new string('ф', 201)),
            ErrorTexts.FullNameLength);
    }

    [Fact]
    public void FullName_Empty_IsRequired()
    {
        AssertErrors(FieldValidators.FullName("   "), ErrorTexts.Required);
    }

    [Fact]
    public void FullName_Trimmed_ThenBoundary()
    {
        // 200 символов после трима (по бокам по пробелу) — валидно.
        AssertErrors(FieldValidators.FullName(" " + new string('ф', 200) + " "));
    }

    [Fact]
    public void GroupName_Boundary100_Valid_101_Invalid()
    {
        AssertErrors(FieldValidators.GroupName(new string('И', 100)));
        AssertErrors(
            FieldValidators.GroupName(new string('И', 101)),
            ErrorTexts.GroupNameLength);
    }

    [Fact]
    public void GroupName_Empty_IsRequired()
    {
        AssertErrors(FieldValidators.GroupName(""), ErrorTexts.Required);
    }

    [Fact]
    public void LabContent_Boundary500_Valid_501_Invalid()
    {
        AssertErrors(FieldValidators.LabContent(new string('с', 500)));
        AssertErrors(
            FieldValidators.LabContent(new string('с', 501)),
            ErrorTexts.LabContent);
    }

    [Fact]
    public void LabContent_Empty_IsRequired()
    {
        AssertErrors(FieldValidators.LabContent(null), ErrorTexts.Required);
    }

    // ------------------------------------------------------------------
    // Ссылка: префикс http(s), 1–1000 после трима (глоссарий v2.2, ISS-015), пусто → валидно (null)
    // ------------------------------------------------------------------

    [Fact]
    public void AssignmentUrl_Boundary1000_Valid_1001_Invalid()
    {
        var max = "https://" + new string('a', 992);
        var over = "https://" + new string('a', 993);

        Assert.Equal(FieldValidators.AssignmentUrlMaxLength, max.Length);
        Assert.Equal(1000, max.Length);
        AssertErrors(FieldValidators.AssignmentUrl(max));
        AssertErrors(FieldValidators.AssignmentUrl(over), ErrorTexts.LabUrlLength);
    }

    [Fact]
    public void AssignmentUrl_OverLength_ErrorTextMatchesGlossary()
    {
        AssertErrors(
            FieldValidators.AssignmentUrl("https://" + new string('a', 993)),
            "Ссылка — не более 1000 символов");
    }

    [Theory]
    [InlineData("ftp://x", "Ссылка должна начинаться с http:// или https://")]
    [InlineData("HTTPS://x", "Ссылка должна начинаться с http:// или https://")]
    [InlineData("example.com", "Ссылка должна начинаться с http:// или https://")]
    public void AssignmentUrl_WrongPrefix_Invalid(string url, string expectedText)
    {
        AssertErrors(FieldValidators.AssignmentUrl(url), expectedText);
    }

    [Fact]
    public void AssignmentUrl_EmptyAfterTrim_IsValid_BecomesNull()
    {
        AssertErrors(FieldValidators.AssignmentUrl(""));
        AssertErrors(FieldValidators.AssignmentUrl("   "));
        AssertErrors(FieldValidators.AssignmentUrl(null));
    }

    [Fact]
    public void AssignmentUrl_Trimmed_ThenBoundary()
    {
        var max = "  https://" + new string('a', 992) + "  ";

        AssertErrors(FieldValidators.AssignmentUrl(max));
    }

    // ------------------------------------------------------------------
    // Номер работы и семестр (параметризация Labs__MaxSemester)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void LabNumber_NonPositive_Invalid(int number)
    {
        AssertErrors(FieldValidators.LabNumber(number), ErrorTexts.LabNumber);
    }

    [Fact]
    public void LabNumber_Positive_Valid_NoUpperBound()
    {
        AssertErrors(FieldValidators.LabNumber(1));
        AssertErrors(FieldValidators.LabNumber(int.MaxValue));
    }

    [Fact]
    public void Semester_Boundaries_WithConfiguredMax()
    {
        AssertErrors(FieldValidators.Semester(1, 10));
        AssertErrors(FieldValidators.Semester(10, 10));
        AssertErrors(FieldValidators.Semester(0, 10), ErrorTexts.LabSemesterText(10));
        AssertErrors(FieldValidators.Semester(11, 10), ErrorTexts.LabSemesterText(10));
    }

    [Fact]
    public void Semester_TextUsesCurrentMaxSemester_NotBakedTen()
    {
        // Критерий «Параметризация текста»: граница 12 из конфигурации теста.
        AssertErrors(FieldValidators.Semester(12, 12));
        var errors = FieldValidators.Semester(13, 12);

        AssertErrors(errors, "Семестр — число от 1 до 12");
        Assert.Contains("12", errors[0], StringComparison.Ordinal);
        Assert.DoesNotContain("10", errors[0], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Строка поиска (FR-019): ровно 200 — валидно, 201 — ошибка
    // ------------------------------------------------------------------

    [Fact]
    public void Search_Boundary200_Valid_201_Invalid()
    {
        AssertErrors(FieldValidators.Search(new string('а', 200)));
        AssertErrors(
            FieldValidators.Search(new string('а', 201)),
            ErrorTexts.SearchLength);
    }

    [Fact]
    public void Search_EmptyOrWhitespace_IsValid()
    {
        AssertErrors(FieldValidators.Search(""));
        AssertErrors(FieldValidators.Search(null));
        AssertErrors(FieldValidators.Search("   "));
    }

    // ------------------------------------------------------------------
    // Контрактные даты: строгий 'YYYY-MM-DD' + календарность
    // ------------------------------------------------------------------

    [Fact]
    public void ContractDate_ValidDate_Parses()
    {
        AssertErrors(FieldValidators.ContractDate("2026-10-01"));

        Assert.True(FieldValidators.TryParseContractDate("2026-10-01", out var parsed));
        Assert.Equal(new DateOnly(2026, 10, 1), parsed);
    }

    [Fact]
    public void ContractDate_February30_Invalid()
    {
        AssertErrors(FieldValidators.ContractDate("2026-02-30"), ErrorTexts.DateInvalid);
        Assert.False(FieldValidators.TryParseContractDate("2026-02-30", out _));
    }

    [Fact]
    public void ContractDate_WrongFormatDotSeparated_Invalid()
    {
        AssertErrors(FieldValidators.ContractDate("01.10.2026"), ErrorTexts.DateInvalid);
        Assert.False(FieldValidators.TryParseContractDate("01.10.2026", out _));
        // Дословный сценарий критерия T-002 «Дата строгая».
        AssertErrors(FieldValidators.ContractDate("20.09.2026"), ErrorTexts.DateInvalid);
        Assert.False(FieldValidators.TryParseContractDate("20.09.2026", out _));
    }

    [Theory]
    [InlineData("2026-13-01")] // месяца 13 не существует
    [InlineData("2026-00-10")] // месяц 0
    [InlineData("2026-10-00")] // дня 0
    [InlineData("2026-10-32")]
    [InlineData("2026-1-01")] // не две цифры месяца
    [InlineData("2026-10-1")] // не две цифры дня
    [InlineData(" 2026-10-01")] // ведущий пробел
    [InlineData("2026-10-01 ")] // хвостовой пробел
    [InlineData("2026-10-01T00:00:00")] // не дата, а метка времени
    [InlineData("")] // пустая строка — не null, неконтрактна
    [InlineData("26-10-01")]
    [InlineData("какая-то строка")]
    public void ContractDate_MalformedInputs_Invalid(string raw)
    {
        AssertErrors(FieldValidators.ContractDate(raw), ErrorTexts.DateInvalid);
        Assert.False(FieldValidators.TryParseContractDate(raw, out _));
    }

    [Fact]
    public void ContractDate_Null_IsValid_OptionalSemantics()
    {
        // null = дата не передана (семантика сброса FR-022) — ошибки нет.
        AssertErrors(FieldValidators.ContractDate(null));
        Assert.False(FieldValidators.TryParseContractDate(null, out _));
    }

    [Fact]
    public void ContractDate_LeapYearBoundaries()
    {
        AssertErrors(FieldValidators.ContractDate("2028-02-29"));
        AssertErrors(FieldValidators.ContractDate("2026-02-28"));
        AssertErrors(FieldValidators.ContractDate("2026-02-29"), ErrorTexts.DateInvalid);
    }

    // ------------------------------------------------------------------
    // Целые числа из строк (зеркало integerValue клиентских валидаторов)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData(" 5 ", 5)]
    [InlineData("007", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void TryParseInteger_ValidDigitStrings(string raw, int expected)
    {
        Assert.True(FieldValidators.TryParseInteger(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("99999999999")]
    [InlineData("+5")]
    [InlineData("١٢٣")] // не-ASCII цифры
    public void TryParseInteger_InvalidInputs(string? raw)
    {
        Assert.False(FieldValidators.TryParseInteger(raw, out _));
    }
}
