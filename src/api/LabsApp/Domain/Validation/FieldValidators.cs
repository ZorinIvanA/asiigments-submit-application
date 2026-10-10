using System.Globalization;
using System.Text.RegularExpressions;

namespace LabsApp.Domain.Validation;

/// <summary>
/// Полевые валидаторы (сервер — истина; зеркало правил и текстов клиентских
/// валидаторов src/client/app/shared/validation/validators.ts, глоссарий v2.2
/// «Тексты ошибок полей»). Каждый валидатор возвращает перечень текстов ошибок
/// поля из <see cref="ErrorTexts"/>; ПУСТОЙ перечень — поле валидно. Правила:
///  - трим до проверки; пустое после трима значение обязательного поля — «Заполните поле»;
///  - пароль — исключение: не триммится, пробел — легальный спецзнак (любой символ,
///    не буква и не цифра), пары сравниваются дословно;
///  - границы длин — по кодовым единицам UTF-16 (string.Length);
///  - пароль ограничен сверху <see cref="PasswordMaxLength"/> = 128 (ISS-016,
///    серверное дополнение словаря password.max): при длине &gt;128 прочие правила
///    не проверяются, при длине &lt;8 нарушенные правила перечисляются ВСЕ сразу.
/// Нарушенные правила возвращаются ВСЕ сразу (как у клиента).
/// </summary>
public static partial class FieldValidators
{
    /// <summary>Верхняя граница длины логина после трима (FR-006: 1–100).</summary>
    public const int LoginMaxLength = 100;

    /// <summary>Верхняя граница длины email после трима, включительно (FR-006: ≤254).</summary>
    public const int EmailMaxLength = 254;

    /// <summary>Минимальная длина пароля (FR-006: ≥8, без трима).</summary>
    public const int PasswordMinLength = 8;

    /// <summary>
    /// Верхняя граница длины пароля в символах (ISS-016): сверхдлинный пароль
    /// отклоняется полевым валидатором ДО любого вызова IPasswordHasher.
    /// </summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Верхняя граница длины ФИО после трима (FR-006: 1–200).</summary>
    public const int FullNameMaxLength = 200;

    /// <summary>Верхняя граница длины названия группы после трима (1–100).</summary>
    public const int GroupNameMaxLength = 100;

    /// <summary>Верхняя граница длины содержания работы после трима (1–500).</summary>
    public const int LabContentMaxLength = 500;

    /// <summary>Верхняя граница длины ссылки после трима, включительно (FR-017, ISS-015: 1000).</summary>
    public const int AssignmentUrlMaxLength = 1000;

    /// <summary>Верхняя граница длины search после трима, включительно (FR-020: 200).</summary>
    public const int SearchMaxLength = 200;

    /// <summary>
    /// Логин (FR-006): 1–100 символов после трима и только [A-Za-z0-9._-];
    /// пробелы внутри и иные символы невалидны; нарушенные правила — вместе.
    /// </summary>
    public static IReadOnlyList<string> Login(string? raw)
    {
        var login = raw?.Trim() ?? string.Empty;
        if (login.Length == 0)
        {
            return [ErrorTexts.Required];
        }

        List<string>? errors = null;
        if (login.Length > LoginMaxLength)
        {
            Add(ref errors, ErrorTexts.LoginLength);
        }

        if (!LoginPattern().IsMatch(login))
        {
            Add(ref errors, ErrorTexts.LoginCharset);
        }

        return errors ?? [];
    }

    /// <summary>
    /// Email (FR-006): формат логин@домен.зона (без пробелов, «@» ровно одна,
    /// домен — метки через точку с непустой зоной) и длина 1–254 после трима.
    /// Формат проверяется раньше длины (как у клиента).
    /// </summary>
    public static IReadOnlyList<string> Email(string? raw)
    {
        var email = raw?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            return [ErrorTexts.Required];
        }

        if (!EmailPattern().IsMatch(email))
        {
            return [ErrorTexts.EmailFormat];
        }

        return email.Length > EmailMaxLength ? [ErrorTexts.EmailLength] : [];
    }

    /// <summary>
    /// Пароль (FR-006): сырая строка БЕЗ трима (ведущие/хвостовые пробелы — часть
    /// пароля, пробел — легальный спецзнак), длина 8–128 включительно
    /// (<see cref="PasswordMinLength"/>/<see cref="PasswordMaxLength"/>). Правила
    /// состава: ≥1 цифра (\d), ≥1 буква (\p{L}), ≥1 спецзнак ([^\p{L}\d]).
    /// При длине &lt;8 нарушенные правила перечисляются ВСЕ сразу (password.min плюс
    /// каждое несработавшее правило состава); при длине &gt;128 — ЕДИНСТВЕННАЯ
    /// ошибка password.max, состав символов не проверяется (FR-006, ISS-016).
    /// null и пустая строка (нестроковое поле JSON трактуется как пустая строка,
    /// FR-006) — длина 0 &lt;8: все четыре текста сразу.
    /// </summary>
    public static IReadOnlyList<string> Password(string? raw)
    {
        var password = raw ?? string.Empty;

        if (password.Length > PasswordMaxLength)
        {
            // FR-006: при длине >128 прочие правила (состав) не проверяются.
            return [ErrorTexts.PasswordMax];
        }

        List<string>? errors = null;
        if (password.Length < PasswordMinLength)
        {
            Add(ref errors, ErrorTexts.PasswordMin);
        }

        if (!password.Any(char.IsDigit))
        {
            Add(ref errors, ErrorTexts.PasswordDigit);
        }

        if (!password.Any(char.IsLetter))
        {
            Add(ref errors, ErrorTexts.PasswordLetter);
        }

        if (!password.Any(c => !char.IsLetter(c) && !char.IsDigit(c)))
        {
            Add(ref errors, ErrorTexts.PasswordSpecial);
        }

        return errors ?? [];
    }

    /// <summary>
    /// Совпадение пароля и повтора (FR-006 «дословно равен»): дословное сравнение
    /// без трима. Пустой пароль или пустой повтор здесь не сравниваются (mismatch
    /// не выдаётся): ошибки пустого password возвращаются <see cref="Password"/>,
    /// обязательность repeatPassword — зона required-проверки контроллера.
    /// </summary>
    public static IReadOnlyList<string> PasswordMatch(string? password, string? repeat)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(repeat))
        {
            return [];
        }

        return string.Equals(password, repeat, StringComparison.Ordinal)
            ? []
            : [ErrorTexts.PasswordMismatch];
    }

    /// <summary>ФИО (FR-006): 1–200 символов после трима.</summary>
    public static IReadOnlyList<string> FullName(string? raw) =>
        BoundedText(raw, FullNameMaxLength, ErrorTexts.FullNameLength);

    /// <summary>Название группы (1–100 символов после трима).</summary>
    public static IReadOnlyList<string> GroupName(string? raw) =>
        BoundedText(raw, GroupNameMaxLength, ErrorTexts.GroupNameLength);

    /// <summary>Содержание работы (FR-017: 1–500 символов после трима).</summary>
    public static IReadOnlyList<string> LabContent(string? raw) =>
        BoundedText(raw, LabContentMaxLength, ErrorTexts.LabContent);

    /// <summary>
    /// Ссылка на задание (FR-017): null/пустая строка после трима — валидно
    /// (нормализуется в null); иначе строка с префиксом http:// или https://
    /// (строчные, как у клиента) и длиной 1–1000 символов после трима.
    /// </summary>
    public static IReadOnlyList<string> AssignmentUrl(string? raw)
    {
        var url = raw?.Trim() ?? string.Empty;
        if (url.Length == 0)
        {
            return [];
        }

        if (!UrlPrefixPattern().IsMatch(url))
        {
            return [ErrorTexts.LabUrl];
        }

        return url.Length > AssignmentUrlMaxLength ? [ErrorTexts.LabUrlLength] : [];
    }

    /// <summary>Номер работы (FR-017): целое &gt; 0, верхней границы нет.</summary>
    public static IReadOnlyList<string> LabNumber(int number) =>
        number > 0 ? [] : [ErrorTexts.LabNumber];

    /// <summary>
    /// Семестр (FR-017): целое 1..maxSemester включительно; текст ошибки
    /// параметризуется ТЕКУЩЕЙ границей Labs__MaxSemester — <see cref="ErrorTexts.LabSemesterText"/>.
    /// </summary>
    public static IReadOnlyList<string> Semester(int semester, int maxSemester) =>
        semester >= 1 && semester <= maxSemester ? [] : [ErrorTexts.LabSemesterText(maxSemester)];

    /// <summary>Строка поиска (FR-020): после трима не длиннее 200 символов; пустая — валидна.</summary>
    public static IReadOnlyList<string> Search(string? raw)
    {
        var search = raw?.Trim() ?? string.Empty;
        return search.Length > SearchMaxLength ? [ErrorTexts.SearchLength] : [];
    }

    /// <summary>
    /// Контрактная дата (FR-021): null — дата не передана (валидно, семантика сброса);
    /// непустое значение обязано быть строгой 'YYYY-MM-DD' календарно корректной датой.
    /// </summary>
    public static IReadOnlyList<string> ContractDate(string? raw) =>
        raw is not null && !TryParseContractDate(raw, out _) ? [ErrorTexts.DateInvalid] : [];

    /// <summary>
    /// Строгий разбор контрактной даты 'YYYY-MM-DD': формат ровно 4-2-2 цифры
    /// (никаких пробелов, точек и времени) плюс календарная корректность
    /// ('2026-02-30', '2026-13-01', '01.10.2026' — невалидны).
    /// </summary>
    public static bool TryParseContractDate(string? raw, out DateOnly value)
    {
        value = default;
        return raw is not null
            && ContractDatePattern().IsMatch(raw)
            && DateOnly.TryParseExact(
                raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// Целое число из строкового значения (зеркало integerValue клиентских валидаторов):
    /// только цифры после трима; «-3», «2.5», «abc», пустое, переполнение int — невалидны.
    /// </summary>
    public static bool TryParseInteger(string? raw, out int value)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !trimmed.All(char.IsAsciiDigit))
        {
            value = 0;
            return false;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Накопление ошибок: список создаётся лениво, без аллокаций для валидного поля.</summary>
    private static void Add(ref List<string>? errors, string text)
    {
        (errors ??= []).Add(text);
    }

    /// <summary>Обязательное строковое поле с верхней границей длины после трима.</summary>
    private static IReadOnlyList<string> BoundedText(string? raw, int maxLength, string errorText)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return [ErrorTexts.Required];
        }

        return value.Length > maxLength ? [errorText] : [];
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex LoginPattern();

    [GeneratedRegex(@"^[^\s@]+@([^\s@.]+\.)+[^\s@.]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^https?://")]
    private static partial Regex UrlPrefixPattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex ContractDatePattern();
}
