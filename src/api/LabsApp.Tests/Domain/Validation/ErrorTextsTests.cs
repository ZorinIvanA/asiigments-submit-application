using LabsApp.Domain.Validation;

namespace LabsApp.Tests.Domain.Validation;

/// <summary>
/// Сверка словаря ErrorTexts с глоссарием v2.2 ПОБАЙТОВО (T-002): message-тексты —
/// дословно «Сообщения верхнего уровня ошибок API»; тексты полевых ошибок — дословно
/// «Тексты ошибок полей»; semester — параметризуемый шаблон «Семестр — число от
/// 1 до {Labs__MaxSemester}» (рендер 10 совпадает с ERROR_TEXTS клиента побайтово).
/// Сервер НЕ выдаёт: «Код должен состоять из 6 цифр» (клиентская пред-валидация)
/// и текст 413 «Тело запроса слишком большое» — инфраструктурные отклонения Kestrel
/// до конвейера отвечают телом фреймворка, словарный текст для них не вводится
/// (FR-023, граница ISS-016/OQ-004).
/// </summary>
public sealed class ErrorTextsTests
{
    [Fact]
    public void MessageTexts_AreVerbatimFromGlossaryV22()
    {
        Assert.Equal("Данные заполнены неверно", ErrorTexts.InvalidData);
        Assert.Equal("Не авторизован", ErrorTexts.Unauthorized);
        Assert.Equal("Неверный логин или пароль", ErrorTexts.InvalidCredentials);
        Assert.Equal("Доступ запрещён", ErrorTexts.Forbidden);
        Assert.Equal("Не найдено", ErrorTexts.NotFound);
        Assert.Equal("Код восстановления не подходит", ErrorTexts.RecoveryCodeRejected);
        Assert.Equal("Ссылка восстановления недействительна или истекла", ErrorTexts.ResetTokenInvalid);
        Assert.Equal("Неверный текущий пароль", ErrorTexts.WrongCurrentPassword);
        Assert.Equal("Пользователь с таким логином уже существует", ErrorTexts.DuplicateLogin);
        Assert.Equal("Пользователь с таким email уже существует", ErrorTexts.DuplicateEmail);
        Assert.Equal("Лабораторная с таким номером уже есть в семестре", ErrorTexts.DuplicateLab);
        Assert.Equal("Группа с таким названием уже существует", ErrorTexts.DuplicateGroup);
        Assert.Equal("Слишком много попыток. Повторите позже", ErrorTexts.RateLimited);
        Assert.Equal("Лабораторная не найдена", ErrorTexts.LabNotFound);
        Assert.Equal("Группа не найдена", ErrorTexts.GroupNotFound);
        Assert.Equal("Студент не найден", ErrorTexts.StudentNotFound);
    }

    [Fact]
    public void FieldTexts_AreVerbatimFromGlossaryV22()
    {
        // Побайтовая сверка с глоссарием v2.2 «Тексты ошибок полей»:
        // lab.semester в словаре нет (параметрический шаблон), «code» нет
        // (сервером не выдаётся), lab.url.length — граница 1000.
        var expected = new Dictionary<string, string>
        {
            ["required"] = "Заполните поле",
            ["password.min"] = "Пароль должен содержать не менее 8 символов",
            ["password.digit"] = "Пароль должен содержать хотя бы одну цифру",
            ["password.letter"] = "Пароль должен содержать хотя бы одну букву",
            ["password.special"] = "Пароль должен содержать хотя бы один специальный знак",
            ["password.mismatch"] = "Пароли не совпадают",
            ["password.max"] = "Пароль — не более 128 символов",
            ["email"] = "Введите корректный email",
            ["email.length"] = "Email — не более 254 символов",
            ["login"] = "Логин — от 1 до 100 символов",
            ["login.charset"] = "Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание",
            ["lab.number"] = "Номер должен быть положительным числом",
            ["lab.content"] = "Содержание — от 1 до 500 символов",
            ["lab.url"] = "Ссылка должна начинаться с http:// или https://",
            ["lab.url.length"] = "Ссылка — не более 1000 символов",
            ["group.name"] = "Название группы — от 1 до 100 символов",
            ["fullName"] = "ФИО — от 1 до 200 символов",
            ["date"] = "Дата должна быть строкой в формате ГГГГ-ММ-ДД",
            ["search"] = "Поиск — не более 200 символов",
        };

        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            ErrorTexts.FieldTexts.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void FieldTexts_DoesNotContainServerForbiddenCodeText()
    {
        // «Код должен состоять из 6 цифр» — исключительно клиентская пред-валидация;
        // ветка errors.code в серверном контракте отсутствует.
        Assert.False(ErrorTexts.FieldTexts.ContainsKey("code"));
    }

    [Fact]
    public void PasswordMax_IsServerOnlyText()
    {
        Assert.Equal("password.max", ErrorTexts.KeyPasswordMax);
        Assert.Equal("Пароль — не более 128 символов", ErrorTexts.PasswordMax);
        Assert.Equal(ErrorTexts.PasswordMax, ErrorTexts.FieldTexts[ErrorTexts.KeyPasswordMax]);
    }

    [Fact]
    public void LabUrlLength_IsThousandSymbols()
    {
        // Граница ссылки v2.2: 1000, текст побайтово глоссария (FR-017, ISS-015).
        Assert.Equal("lab.url.length", ErrorTexts.KeyLabUrlLength);
        Assert.Equal("Ссылка — не более 1000 символов", ErrorTexts.LabUrlLength);
        Assert.Equal(ErrorTexts.LabUrlLength, ErrorTexts.FieldTexts[ErrorTexts.KeyLabUrlLength]);
    }

    [Fact]
    public void LabSemesterText_SubstitutesCurrentConfiguration()
    {
        // Критерий «semester-шаблон»: Labs__MaxSemester=12 → рендер «…до 12»,
        // зашитого «10» нет.
        var text = ErrorTexts.LabSemesterText(12);

        Assert.Equal("Семестр — число от 1 до 12", text);
        Assert.Contains("12", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LabSemesterText_DefaultTen_MatchesClientTextByteForByte()
    {
        // При умолчании 10 рендер совпадает с ERROR_TEXTS клиента побайтово.
        Assert.Equal("Семестр — число от 1 до 10", ErrorTexts.LabSemesterText(10));
        Assert.Equal("Семестр — число от 1 до 1", ErrorTexts.LabSemesterText(1));
        Assert.Equal("Семестр — число от 1 до 100", ErrorTexts.LabSemesterText(100));
    }

    [Fact]
    public void Dictionary_DoesNotContainBodyTooLargeText()
    {
        // FR-023 (граница конверта ISS-016/OQ-004): 413 инфраструктурного лимита
        // Kestrel отвечает телом фреймворка — словарный текст для него не вводится.
        string[] messageTexts =
        [
            ErrorTexts.InvalidData,
            ErrorTexts.Unauthorized,
            ErrorTexts.InvalidCredentials,
            ErrorTexts.Forbidden,
            ErrorTexts.NotFound,
            ErrorTexts.RecoveryCodeRejected,
            ErrorTexts.ResetTokenInvalid,
            ErrorTexts.WrongCurrentPassword,
            ErrorTexts.DuplicateLogin,
            ErrorTexts.DuplicateEmail,
            ErrorTexts.DuplicateLab,
            ErrorTexts.DuplicateGroup,
            ErrorTexts.RateLimited,
            ErrorTexts.LabNotFound,
            ErrorTexts.GroupNotFound,
            ErrorTexts.StudentNotFound,
        ];

        Assert.DoesNotContain(
            "Тело запроса слишком большое",
            ErrorTexts.FieldTexts.Values,
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            "Тело запроса слишком большое",
            messageTexts,
            StringComparer.Ordinal);
    }

    [Fact]
    public void FieldTexts_DoesNotContainBakedSemesterText()
    {
        // В справочном словаре нет параметрического ключа — зашитого «…до 10» не существует.
        Assert.False(ErrorTexts.FieldTexts.ContainsKey("lab.semester"));
    }
}
