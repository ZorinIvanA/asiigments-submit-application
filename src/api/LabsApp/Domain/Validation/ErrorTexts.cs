using System.Globalization;

namespace LabsApp.Domain.Validation;

/// <summary>
/// Единый словарь ошибок API (FR-023, IF-001) — сверка с глоссарием спеки v2.2
/// («Сообщения верхнего уровня ошибок API», «Тексты ошибок полей») ДОСЛОВНО.
/// message-тексты — дословно глоссарий; texts полевых ошибок дословно совпадают
/// с ERROR_TEXTS клиента там, где проверка дублируется клиентской пред-валидацией;
/// серверные тексты (password.max, lab.url.length, date, search) существуют только
/// на сервере и доходят до пользователя из тела 400. Особенности v2.2:
///  - «Код должен состоять из 6 цифр» на сервере НЕ выдаётся — исключительно
///    клиентская пред-валидация поля code; любое несовпадение кода восстановления
///    покрывается верхнеуровневым 400 «Код восстановления не подходит»;
///  - текста 413 («Тело запроса слишком большое») в словаре НЕТ: инфраструктурные
///    отклонения Kestrel до конвейера (413/400) отвечают телом фреймворка по
///    умолчанию и требованию конверта не подчиняются (FR-023, граница ISS-016/OQ-004);
///  - 404 неизвестного /api-маршрута — {'message':'Не найдено'} (<see cref="NotFound"/>).
/// Текст поля с конфигурируемой границей (semester) — шаблон «Семестр — число от 1 до
/// {Labs__MaxSemester}» — рендерится подстановкой ТЕКУЩЕГО Labs__MaxSemester
/// (<see cref="LabSemesterText"/>); зашивка умолчания 10 не существует.
/// </summary>
public static class ErrorTexts
{
    // ------------------------------------------------------------------
    // message-тексты (глоссарий v2.2 «Сообщения верхнего уровня ошибок API»)
    // ------------------------------------------------------------------

    /// <summary>Текст 400: нарушение правил полей (глоссарий v2.2).</summary>
    public const string InvalidData = "Данные заполнены неверно";

    /// <summary>Текст 401: нет/просрочена/невалидна access-cookie (глоссарий v2.2).</summary>
    public const string Unauthorized = "Не авторизован";

    /// <summary>Текст 401: неверная пара логин/пароль (глоссарий v2.2, единый отказ входа).</summary>
    public const string InvalidCredentials = "Неверный логин или пароль";

    /// <summary>Текст 403: роль не подходит под матрицу авторизации (глоссарий v2.2).</summary>
    public const string Forbidden = "Доступ запрещён";

    /// <summary>
    /// Текст 404: неизвестный маршрут под /api — {'message':'Не найдено'}
    /// (FR-023, ADR-010).
    /// </summary>
    public const string NotFound = "Не найдено";

    /// <summary>Текст 404: работа не существует (FR-017).</summary>
    public const string LabNotFound = "Лабораторная не найдена";

    /// <summary>Текст 404: группа не существует (FR-020/FR-021; ветка Invalid у PUT /students/{id}/group).</summary>
    public const string GroupNotFound = "Группа не найдена";

    /// <summary>Текст 404: пользователь не существует или не студент (FR-020/FR-021).</summary>
    public const string StudentNotFound = "Студент не найден";

    /// <summary>Текст 400: неверный/просроченный/использованный/аннулированный код (глоссарий v2.2).</summary>
    public const string RecoveryCodeRejected = "Код восстановления не подходит";

    /// <summary>Текст 400: reset-токен неверный/просроченный/уже использованный (FR-014, глоссарий v2.2).</summary>
    public const string ResetTokenInvalid = "Ссылка восстановления недействительна или истекла";

    /// <summary>Текст 400: неверный текущий пароль при смене из профиля (FR-016, глоссарий v2.2).</summary>
    public const string WrongCurrentPassword = "Неверный текущий пароль";

    /// <summary>Текст 409: занятый login (ci) при регистрации (FR-006; проверяется раньше email).</summary>
    public const string DuplicateLogin = "Пользователь с таким логином уже существует";

    /// <summary>Текст 409: занятый email (ci) при регистрации/смене в профиле (FR-006, глоссарий v2.2).</summary>
    public const string DuplicateEmail = "Пользователь с таким email уже существует";

    /// <summary>Текст 409: пара (semester, number) существует у другой записи (FR-017).</summary>
    public const string DuplicateLab = "Лабораторная с таким номером уже есть в семестре";

    /// <summary>Текст 409: название группы занято (ci) (глоссарий v2.2).</summary>
    public const string DuplicateGroup = "Группа с таким названием уже существует";

    /// <summary>Текст 429: превышен лимит частоты register/login/recovery (FR-004, глоссарий v2.2).</summary>
    public const string RateLimited = "Слишком много попыток. Повторите позже";

    // ------------------------------------------------------------------
    // Тексты полевых ошибок: глоссарий v2.2 «Тексты ошибок полей»
    // ------------------------------------------------------------------

    /// <summary>Ключ required: пусто либо строка из одних пробелов.</summary>
    public const string KeyRequired = "required";

    /// <summary>Текст required (дословно глоссарий v2.2).</summary>
    public const string Required = "Заполните поле";

    /// <summary>Текст password.min (дословно глоссарий v2.2).</summary>
    public const string PasswordMin = "Пароль должен содержать не менее 8 символов";

    /// <summary>Текст password.digit (дословно глоссарий v2.2).</summary>
    public const string PasswordDigit = "Пароль должен содержать хотя бы одну цифру";

    /// <summary>Текст password.letter (дословно глоссарий v2.2).</summary>
    public const string PasswordLetter = "Пароль должен содержать хотя бы одну букву";

    /// <summary>Текст password.special (дословно глоссарий v2.2).</summary>
    public const string PasswordSpecial = "Пароль должен содержать хотя бы один специальный знак";

    /// <summary>Текст password.mismatch (дословно глоссарий v2.2).</summary>
    public const string PasswordMismatch = "Пароли не совпадают";

    /// <summary>Текст email — формат (дословно глоссарий v2.2).</summary>
    public const string EmailFormat = "Введите корректный email";

    /// <summary>Текст email.length (дословно глоссарий v2.2).</summary>
    public const string EmailLength = "Email — не более 254 символов";

    /// <summary>Текст login — длина (дословно глоссарий v2.2).</summary>
    public const string LoginLength = "Логин — от 1 до 100 символов";

    /// <summary>Текст login.charset (дословно глоссарий v2.2).</summary>
    public const string LoginCharset =
        "Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание";

    /// <summary>Текст lab.number (дословно глоссарий v2.2).</summary>
    public const string LabNumber = "Номер должен быть положительным числом";

    /// <summary>Текст lab.content (дословно глоссарий v2.2).</summary>
    public const string LabContent = "Содержание — от 1 до 500 символов";

    /// <summary>Текст lab.url — префикс (дословно глоссарий v2.2).</summary>
    public const string LabUrl = "Ссылка должна начинаться с http:// или https://";

    /// <summary>Текст group.name (дословно глоссарий v2.2).</summary>
    public const string GroupNameLength = "Название группы — от 1 до 100 символов";

    /// <summary>Текст fullName (дословно глоссарий v2.2).</summary>
    public const string FullNameLength = "ФИО — от 1 до 200 символов";

    // ------------------------------------------------------------------
    // Серверные тексты (в ERROR_TEXTS клиента отсутствуют — глоссарий v2.2:
    // «существуют только на сервере и доходят до пользователя из тела 400»)
    // ------------------------------------------------------------------

    /// <summary>Ключ password.max — верхняя граница длины пароля 128.</summary>
    public const string KeyPasswordMax = "password.max";

    /// <summary>Текст password.max (дословно глоссарий v2.2).</summary>
    public const string PasswordMax = "Пароль — не более 128 символов";

    /// <summary>Ключ lab.url.length — граница длины ссылки (FR-017, ISS-015).</summary>
    public const string KeyLabUrlLength = "lab.url.length";

    /// <summary>Текст lab.url.length: длина ссылки &gt;1000 после трима (дословно глоссарий v2.2).</summary>
    public const string LabUrlLength = "Ссылка — не более 1000 символов";

    /// <summary>Ключ date — контрактные даты сдачи.</summary>
    public const string KeyDate = "date";

    /// <summary>Текст date (дословно глоссарий v2.2, поля submitDate/defenseDate).</summary>
    public const string DateInvalid = "Дата должна быть строкой в формате ГГГГ-ММ-ДД";

    /// <summary>Ключ search — строка поиска.</summary>
    public const string KeySearch = "search";

    /// <summary>Текст search (дословно глоссарий v2.2).</summary>
    public const string SearchLength = "Поиск — не более 200 символов";

    /// <summary>Ключ lab.semester — параметризуется текущим Labs__MaxSemester (глоссарий v2.2).</summary>
    public const string KeyLabSemester = "lab.semester";

    /// <summary>
    /// Рендер шаблона «Семестр — число от 1 до {Labs__MaxSemester}» подстановкой
    /// ТЕКУЩЕЙ конфигурации: зашивка умолчания 10 запрещена — текст всегда строится
    /// из конфигурации. При MaxSemester=10 рендер совпадает с ERROR_TEXTS клиента
    /// побайтово.
    /// </summary>
    public static string LabSemesterText(int maxSemester) =>
        $"Семестр — число от 1 до {maxSemester.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Словарь «ключ поля → текст» — глоссарий v2.2 «Тексты ошибок полей»;
    /// ключи совпадают с клиентскими там, где проверка дублируется пред-валидацией.
    /// lab.semester здесь нет: его текст параметрический (<see cref="LabSemesterText"/>);
    /// ключа «code» здесь тоже нет — сервером не выдаётся (шапка класса).
    /// </summary>
    public static IReadOnlyDictionary<string, string> FieldTexts { get; } =
        new Dictionary<string, string>
        {
            [KeyRequired] = Required,
            ["password.min"] = PasswordMin,
            ["password.digit"] = PasswordDigit,
            ["password.letter"] = PasswordLetter,
            ["password.special"] = PasswordSpecial,
            ["password.mismatch"] = PasswordMismatch,
            [KeyPasswordMax] = PasswordMax,
            ["email"] = EmailFormat,
            ["email.length"] = EmailLength,
            ["login"] = LoginLength,
            ["login.charset"] = LoginCharset,
            ["lab.number"] = LabNumber,
            ["lab.content"] = LabContent,
            ["lab.url"] = LabUrl,
            [KeyLabUrlLength] = LabUrlLength,
            ["group.name"] = GroupNameLength,
            ["fullName"] = FullNameLength,
            [KeyDate] = DateInvalid,
            [KeySearch] = SearchLength,
        };
}
