using System.Text;
using System.Text.RegularExpressions;

namespace LabsApp.IntegrationTests.B18.Infrastructure;

/// <summary>Статус-маркер ветки KDF-матрицы (FR-027 гейт (1), FR-007).</summary>
public enum KdfStatus
{
    Ok200,
    Unauthorized401,
    TooManyRequests429,
}

/// <summary>
/// Ветка KDF-матрицы входа. Предикат покрытия (CR-001/CR-002 арбитража a-034
/// и раунда доработки): В ПРЕДЕЛАХ ОДНОГО тест-метода/блока совместно
/// встречаются маркер статуса (200/401/429 либо
/// HttpStatusCode.OK/Unauthorized/TooManyRequests), И отличитель ветки —
/// искомый вне вхождений <see cref="MaskBeforeMatch"/> (нейтрализация
/// вложенности «known»⊂«unknown», «известн»⊂«неизвестн»), И маркер
/// утверждения Δkdf=1 (см. BackendGateProbe.DeltaKdfMarkers). Одиночные
/// универсальные токены («success», «before», «lock», голые цифры без
/// отличителя, голый «snapshot» любого снимка) доказательством не считаются.
/// </summary>
/// <param name="Id">Технический идентификатор ветки.</param>
/// <param name="Title">Человекочитаемое название ветки (для отчётов о провале).</param>
/// <param name="Status">Ожидаемый статус ветки.</param>
/// <param name="DistinguisherAnyOf">Отличитель ветки (достаточно одного маркера).</param>
/// <param name="DistinguisherDescription">Описание отличителя для сообщений.</param>
/// <param name="MaskBeforeMatch">Вхождения этих токенов маскируются в тексте
/// блока ДО поиска отличителя; у веток без вложенных отличителей — пустой
/// список.</param>
public sealed record KdfBranch(
    string Id,
    string Title,
    KdfStatus Status,
    IReadOnlyList<string> DistinguisherAnyOf,
    string DistinguisherDescription,
    IReadOnlyList<string> MaskBeforeMatch);

/// <summary>Требование покрытия гейта FR-027 (базовый тип).</summary>
public abstract class GateRequirement
{
    protected GateRequirement(string description) => Description = description;

    /// <summary>Человекочитаемое описание для отчёта о провале.</summary>
    public string Description { get; }
}

/// <summary>
/// Требование «в коде тестов (без комментариев) встречается хотя бы один из
/// маркеров». Осмысленные маркеры: подстрока «lab» в имени проекта/неймспейсе
/// или голые цифры доказательством не считаются (CR-002 арбитража a-034) —
/// цифровые маркеры ищутся по границе слова.
/// </summary>
public sealed class AnyOfRequirement : GateRequirement
{
    public AnyOfRequirement(string description, IReadOnlyList<string> anyOf) : base(description) =>
        AnyOf = anyOf;

    public IReadOnlyList<string> AnyOf { get; }
}

/// <summary>
/// Требование «существует тест-метод/блок, содержащий СОВМЕСТНО маркер из
/// AnyOfA и маркер из AnyOfB» — невакуумная осмысленная комбинация (CR-002).
/// </summary>
public sealed class BlockJointRequirement : GateRequirement
{
    public BlockJointRequirement(
        string description,
        IReadOnlyList<string> anyOfA,
        IReadOnlyList<string> anyOfB) : base(description)
    {
        AnyOfA = anyOfA;
        AnyOfB = anyOfB;
    }

    public IReadOnlyList<string> AnyOfA { get; }

    public IReadOnlyList<string> AnyOfB { get; }
}

/// <summary>Обязательный гейт FR-027: номер пункта, название, требования.</summary>
public sealed record MandatoryGate(int Number, string Title, IReadOnlyList<GateRequirement> Requirements);

/// <summary>Результат оценки одного гейта: список непокрытых требований (пусто — покрыт).</summary>
public sealed record GateEvaluationResult(MandatoryGate Gate, IReadOnlyList<string> MissingRequirements)
{
    public bool IsCovered => MissingRequirements.Count == 0;
}

/// <summary>
/// Поиск и статический анализ тестов обязательных гейтов FR-027 в исходниках
/// src/api/LabsApp.Tests. Гейт (1) KDF-матрицы входа ищется по совместному
/// признаку «/auth/login» + «kdf» в файле; покрытие веток оценивается
/// ПОМЕТОДНО (в пределах одного тест-метода/блока; блоки строятся ПОФАЙЛОВО —
/// доработка CR-002), предикаты шести веток
/// попарно различимы (CR-001: вложенность отличителей нейтрализуется
/// маскированием) и требуют маркер утверждения Δkdf=1 в том же блоке
/// (доработка CR-002). Пункты (2)–(8) оцениваются невакуумными
/// комбинациями маркеров: комментарии вырезаются, голые цифры/подстроки имени
/// проекта доказательством не считаются (CR-002). Негативные пробы
/// различимости выполняются на контрольных текстах (см. метатесты
/// Ts181/Ts182).
/// </summary>
public static class BackendGateProbe
{
    /// <summary>Маркер пути входа POST /auth/login.</summary>
    public const string LoginPathMarker = "/auth/login";

    /// <summary>
    /// Отличитель «известный логин» (доработка CR-003: набор СУЖЕН до
    /// контекстных токенов логина). Удалены прежние токены «exists» и
    /// «существ»: «exists» совпадал с обычными идентификаторами кодовой базы
    /// (ExistsNameCi/ExistsPair/groupExists — контракт IF-015) и английскими
    /// сообщениями, «существ» — вложен в «не существует»/«несуществующий»;
    /// блок ветки неизвестного логина с такими вхождениями «доказывал» бы
    /// известную ветку. Токен «existing» оставлен, а его вложенность в
    /// отрицательные формы («nonexisting», «nonexistent», «not exist»)
    /// нейтрализуется маской <see cref="UnknownContextMaskMarkers"/>.
    /// </summary>
    public static IReadOnlyList<string> KnownLoginMarkers { get; } =
        ["известн", "known", "existing"];

    /// <summary>
    /// Отличитель «неизвестный логин». КАК ПОДСТРОКИ наборы маркеров двух
    /// веток НЕ дизъюнктны: «unknown» содержит «known», «неизвестн» содержит
    /// «известн» (CR-001) — вложенность нейтрализуется маскированием
    /// <see cref="UnknownContextMaskMarkers"/> при сопоставлении отличителя
    /// известного логина, а различимость проверяется негативными пробами
    /// метатестов Ts181.
    /// </summary>
    public static IReadOnlyList<string> UnknownLoginMarkers { get; } =
        ["неизвестн", "unknown", "ghost"];

    /// <summary>
    /// Контекстные вхождения, маскируемые ДО поиска отличителя «известный
    /// логин» (CR-001): «unknown» содержит «known», «неизвестн» содержит
    /// «известн» — без маскирования любой блок неизвестного логина
    /// удовлетворял бы отличителю известного (известная 429-ветка
    /// «покрывалась» бы блоком Login_BlockedUnknownUser_…).
    /// ДОРАБОТКА CR-003: маска расширена отрицательными формами существования
    /// («не существует»/«несуществ…», «nonexisting»/«nonexistent»/«not exist»/
    /// «not exists»/«notexists»/«not_exists») — они содержат вложенные
    /// «известн»-родственные остатки прежних маркеров и без маскирования
    /// блок ветки неизвестного логина (сообщения вида «пользователь
    /// не существует», вспомогательные идентификаторы NonExistingUser)
    /// «доказывал» бы известную ветку.
    /// </summary>
    public static IReadOnlyList<string> UnknownContextMaskMarkers { get; } =
    [
        "unknown", "неизвестн",
        "не существует", "несуществ",
        "nonexisting", "nonexistent", "not exist", "not exists", "notexists", "not_exists",
    ];

    /// <summary>Отличитель «исчерпанный ключ / окно лимитера».</summary>
    public static IReadOnlyList<string> BlockedKeyMarkers { get; } =
        ["заблокир", "block", "исчерпан", "exhaust", "лимитер", "limiter", "окн", "window", "метк", "mark", "заперт"];

    /// <summary>
    /// Маркеры утверждения Δkdf=1 — СПЕЦИФИЧНЫЕ формы утверждений о снимках
    /// счётчика KDF (класс TotalDelta/CallerDelta/KdfSnapshot; доработка
    /// CR-002): голые «snapshot»/«снимок» ИСКЛЮЧЕНЫ — любой посторонний вызов
    /// Snapshot() вне утверждений (хелпер, снимок другого контура) больше не
    /// считается доказательством утверждения Δkdf.
    /// </summary>
    public static IReadOnlyList<string> DeltaKdfMarkers { get; } =
    [
        "Δkdf", "deltakdf", "delta_kdf", "kdfdelta", "kdf_delta", "kdf delta",
        "totaldelta", "total_delta", "callerdelta", "caller_delta",
        "kdfsnapshot", "kdf_snapshot", "kdf snapshot",
        "kdfcounter", "kdf_counter", "kdf counter", "счётчик kdf",
    ];

    /// <summary>
    /// Шесть веток гейта KDF-матрицы (FR-027 AC «Гейт KDF-матрицы», FR-007
    /// шаги (4)–(6)). Предикаты попарно различимы; у двух 429-веток отличители
    /// РАЗЛИЧНЫ (известный против неизвестного логина); у ветки «успех на
    /// заблокированном ключе» маркер успеха (200) идёт совместно с маркером
    /// исчерпанного ключа/окна лимитера.
    /// </summary>
    public static IReadOnlyList<KdfBranch> KdfMatrixBranches { get; } =
    [
        new("200-known-correct",
            "200 (известный логин + верный пароль)",
            KdfStatus.Ok200, KnownLoginMarkers, "известный логин", UnknownContextMaskMarkers),
        new("401-known-wrong",
            "401 (известный логин + неверный пароль)",
            KdfStatus.Unauthorized401, KnownLoginMarkers, "известный логин", UnknownContextMaskMarkers),
        new("401-unknown",
            "401 (неизвестный логин)",
            KdfStatus.Unauthorized401, UnknownLoginMarkers, "неизвестный логин", []),
        new("429-known",
            "429 (известный логин, заблокированный ключ)",
            KdfStatus.TooManyRequests429, KnownLoginMarkers, "известный логин", UnknownContextMaskMarkers),
        new("429-unknown",
            "429 (неизвестный логин, заблокированный ключ)",
            KdfStatus.TooManyRequests429, UnknownLoginMarkers, "неизвестный логин", []),
        new("200-blocked-key",
            "200 (успех на заблокированном ключе)",
            KdfStatus.Ok200, BlockedKeyMarkers, "исчерпанный ключ/окно лимитера", []),
    ];

    /// <summary>Маршруты домена для гейта (6) — только полные пути, не подстроки имени проекта.</summary>
    public static IReadOnlyList<string> RoutePaths { get; } = ["/labs", "/groups", "/students", "/submissions"];

    /// <summary>
    /// Внутриблочные указатели домена для гейта (6): полный путь маршрута либо
    /// имя харнеса соответствующего эндпойнта (константы URL вынесены в
    /// харнесы вне тест-методов, поэтому в пределах одного блока маршрут
    /// виден как «XEndpointHarness.XEndpoint»).
    /// </summary>
    public static IReadOnlyList<string> DomainEndpointHints { get; } =
    [
        "/labs", "/groups", "/students", "/submissions",
        "labsendpoint", "groupsendpoint", "studentsendpoint", "submissionsendpoint",
    ];

    /// <summary>Маркеры конверта ошибок для гейта (8) — не голые цифры.</summary>
    public static IReadOnlyList<string> EnvelopeMarkers { get; } =
        ["message", "errors", "конверт", "envelope", "problemdetails", "apierror", "errorresponse"];

    /// <summary>
    /// Обязательные гейты FR-027 (пункты (2)–(8); гейт (1) оценивается
    /// ветвевой проверкой KdfMatrixBranches и в списке требований пуст).
    /// </summary>
    public static IReadOnlyList<MandatoryGate> MandatoryGates { get; } =
    [
        new(1, "KDF-матрица входа: Δkdf=1 во всех шести ветках (включая обе 429-ветки и успех на заблокированном ключе)", []),
        new(2, "лимитер: движок окна/лимитера, потолок MaxTrackedKeys + overflow, граница 5/6 попыток с метками окна",
        [
            new AnyOfRequirement("движок окна/лимитера", ["slidingwindow", "limiter", "ratelimit"]),
            new AnyOfRequirement("потолок MaxTrackedKeys + overflow", ["maxtrackedkeys", "overflow"]),
            new BlockJointRequirement(
                "граница 5/6 попыток в связке с попытками/метками окна",
                ["5", "6"],
                ["метк", "mark", "попыт", "attempt", "окн", "window", "лимит", "limit"]),
        ]),
        new(3, "recovery-поток: request (всегда 200, переотправка гасит прежний код, 3/час), confirm (верный/неверный/5-я попытка/неизвестный), reset-password (успех, отказы, отзыв refresh)",
        [
            new BlockJointRequirement(
                "recovery/request: путь + переотправка/3-в-час",
                ["recovery/request", "recoveryrequest", "requestrecovery", "requestcode"],
                ["переотправ", "гасит", "3/час", "всегда", "always", "resent", "reissue", "прежн",
                "resend", "extinguish"]),
            new BlockJointRequirement(
                "recovery/confirm: путь + ветки проверки кода",
                ["recovery/confirm", "recoveryconfirm", "confirmrecovery"],
                ["неверн", "wrong", "попыт", "attempt", "неизвестн", "unknown", "код", "code"]),
            new BlockJointRequirement(
                "reset-password: путь + отзыв/204",
                ["reset-password", "resetpassword", "reset_password"],
                ["отзыв", "revoke", "refresh", "204"]),
        ]),
        new(4, "cookie/токены: флаги cookie, refresh 204/401, logout-отзыв",
        [
            new AnyOfRequirement("cookie / Set-Cookie", ["set-cookie", "cookie"]),
            new AnyOfRequirement("флаги HttpOnly/SameSite/Max-Age/Secure", ["httponly", "samesite", "max-age", "secure"]),
            new AnyOfRequirement("refresh-токены", ["refresh"]),
            new AnyOfRequirement("logout / отзыв", ["logout", "отзыв", "revoke"]),
        ]),
        new(5, "матрица ролей (представитель каждой группы эндпойнтов)",
        [
            new AnyOfRequirement("роли", ["role", "роли", "роль"]),
            new AnyOfRequirement("серверный запрет 403/Forbid", ["403", "forbid", "запрещен"]),
            new AnyOfRequirement("представители admin/teacher/student", ["admin", "teacher", "student"]),
        ]),
        new(6, "labs/groups/students/submissions: happy + 404 + 409 + валидация + нормализация page",
        [
            new AnyOfRequirement("маршрут /labs", ["/labs"]),
            new AnyOfRequirement("маршрут /groups", ["/groups"]),
            new AnyOfRequirement("маршрут /students", ["/students"]),
            new AnyOfRequirement("маршрут /submissions", ["/submissions"]),
            new BlockJointRequirement("404 на маршрутах домена", ["404", "NotFound"], DomainEndpointHints),
            new BlockJointRequirement("409 на маршрутах домена", ["409", "Conflict"], DomainEndpointHints),
            new BlockJointRequirement("валидация 400 на маршрутах домена", ["400", "валидаци", "validation", "BadRequest"], DomainEndpointHints),
            new BlockJointRequirement("нормализация page", ["page", "страниц"], ["нормализ", "normalize"]),
        ]),
        new(7, "сид: идемпотентность",
        [
            new AnyOfRequirement("сид", ["seed"]),
            new BlockJointRequirement(
                "идемпотентность повторного прогона сида",
                ["seed"],
                ["идемпотент", "idempotent", "twice", "дважды", "повторн", "second"]),
        ]),
        new(8, "конверт ошибок: форма 400/404/500 (в контексте конверта, не голые цифры)",
        [
            new BlockJointRequirement("400 в контексте конверта (message/errors)", ["400"], EnvelopeMarkers),
            new BlockJointRequirement("404 в контексте конверта", ["404", "NotFound"], EnvelopeMarkers),
            new BlockJointRequirement("500 в контексте конверта", ["500"], EnvelopeMarkers),
        ]),
    ];

    /// <summary>
    /// Файлы гейта KDF-матрицы входа: содержат и путь POST /auth/login,
    /// и упоминание KDF (гейт (1) FR-027).
    /// </summary>
    public static IReadOnlyList<string> FindKdfMatrixGateFiles()
    {
        var gateFiles = new List<string>();
        foreach (var file in BackendSuitePaths.TestSourceFiles())
        {
            var content = File.ReadAllText(file);
            if (content.Contains(LoginPathMarker, StringComparison.OrdinalIgnoreCase)
                && content.Contains("kdf", StringComparison.OrdinalIgnoreCase))
            {
                gateFiles.Add(file);
            }
        }

        return gateFiles;
    }

    /// <summary>Конкатенация содержимого файлов (для any-of-поиска маркеров).</summary>
    public static string UnionText(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            builder.AppendLine(File.ReadAllText(file));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Вырезает комментарии // и /* */ (с защитой схемы URI «://» от ложного
    /// распознавания комментарием). Комментарий — не доказательство покрытия:
    /// «429 упомянут лишь в комментарии» ветку не покрывает (негативная проба
    /// TS-181).
    /// </summary>
    public static string StripComments(string source)
    {
        var withoutBlockComments = Regex.Replace(
            source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);

        var result = new StringBuilder(withoutBlockComments.Length);
        foreach (var line in withoutBlockComments.Split('\n'))
        {
            result.AppendLine(RemoveLineComment(line));
        }

        return result.ToString();
    }

    private static string RemoveLineComment(string line)
    {
        var searchFrom = 0;
        int index;
        while ((index = line.IndexOf("//", searchFrom, StringComparison.Ordinal)) >= 0)
        {
            var isUriScheme = index > 0 && line[index - 1] == ':';
            if (!isUriScheme)
            {
                return line[..index];
            }

            searchFrom = index + 2;
        }

        return line;
    }

    /// <summary>
    /// Делит код ОДНОГО файла (уже без комментариев) на блоки тест-методов по
    /// атрибутам [Fact]/[Theory]. Содержимое до первого атрибута (usings,
    /// поля класса, хелперы) блоком НЕ считается — маркер «где-то в файле»
    /// доказательством ветки не является: покрытие оценивается в пределах
    /// одного блока. Для корпуса из нескольких файлов используйте перегрузку
    /// <see cref="SplitIntoMethodBlocks(IReadOnlyList{string})"/> — подавать
    /// сюда конкатенацию файлов НЕЛЬЗЯ (доработка CR-002).
    /// </summary>
    public static IReadOnlyList<string> SplitIntoMethodBlocks(string codeWithoutComments)
    {
        var blocks = new List<string>();
        var current = new StringBuilder();
        foreach (var line in codeWithoutComments.Split('\n'))
        {
            if (TestMethodAttributeRegex.IsMatch(line))
            {
                if (current.Length > 0)
                {
                    blocks.Add(current.ToString());
                }

                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            blocks.Add(current.ToString());
        }

        // Первый фрагмент — заголовок файла до первого [Fact]/[Theory].
        return blocks.Count <= 1 ? [] : blocks.Skip(1).ToList();
    }

    /// <summary>
    /// Поблочное разбиение КОРПУСА файлов (доработка CR-002): КАЖДЫЙ файл
    /// делится на блоки ОТДЕЛЬНО, объединяются уже готовые блоки. Конкатенация
    /// исходных текстов перед разбиением недопустима: Skip(1) отбрасывал
    /// заголовок только ПЕРВОГО файла, и хвост последнего тест-метода файла N
    /// склеивался с заголовком (и хелперами до первого [Fact]) файла N+1 —
    /// блок мог удовлетворять предикату ветки маркерами из РАЗНЫХ файлов
    /// (фактический вектор: Hosting/TestSession.cs — содержит «/auth/login»
    /// и «kdf», но не содержит ни одного [Fact]/[Theory] и целиком приклеивался
    /// к последнему тест-блоку AuthEndpointTests.cs).
    /// </summary>
    public static IReadOnlyList<string> SplitIntoMethodBlocks(IReadOnlyList<string> fileCodesWithoutComments) =>
        fileCodesWithoutComments.SelectMany(SplitIntoMethodBlocks).ToList();

    private static readonly Regex TestMethodAttributeRegex =
        new(@"^\s*\[(Fact|Theory)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Список НЕ покрытых веток KDF-матрицы для текста гейта: ветка считается
    /// покрытой, только если в одном блоке (тест-методе) совместно найдены
    /// маркер статуса, отличитель ветки (искомый вне вхождений
    /// <see cref="KdfBranch.MaskBeforeMatch"/> — нейтрализация вложенности
    /// «known»⊂«unknown», CR-001) и маркер утверждения Δkdf=1 (третий
    /// конъюнкт предиката, доработка CR-002).
    /// Перегрузка для одиночного контрольного текста (негативные пробы);
    /// для корпуса файлов используйте перегрузку с списка текстов — разбиение
    /// на блоки идёт ПОФАЙЛОВО (доработка CR-002).
    /// </summary>
    public static IReadOnlyList<string> MissingKdfBranches(string rawGateText) =>
        MissingKdfBranches([rawGateText]);

    /// <summary>
    /// Список НЕ покрытых веток KDF-матрицы для КОРПУСА текстов файлов:
    /// блоки строятся пофайлово (<see
    /// cref="SplitIntoMethodBlocks(IReadOnlyList{string})"/>, доработка
    /// CR-002) и объединяются — маркеры ветки из разных файлов не складываются
    /// в один блок.
    /// </summary>
    public static IReadOnlyList<string> MissingKdfBranches(IReadOnlyList<string> fileTexts)
    {
        var blocks = SplitIntoMethodBlocks(fileTexts.Select(StripComments).ToList());
        return KdfMatrixBranches
            .Where(branch => !blocks.Any(block => BranchCoveredInBlock(branch, block)))
            .Select(branch => branch.Title)
            .ToList();
    }

    private static bool BranchCoveredInBlock(KdfBranch branch, string block) =>
        ContainsStatus(block, branch.Status)
        && branch.DistinguisherAnyOf.Any(marker =>
            ContainsToken(MaskAll(block, branch.MaskBeforeMatch), marker))
        && DeltaKdfAssertedInBlock(block);

    /// <summary>
    /// Маскирует вхождения маркеров (без учёта регистра) пробами той же длины
    /// ДО поиска отличителя (CR-001): после маскирования «unknown»/
    /// «неизвестн» вложенные «known»/«известн» перестают находиться — блок
    /// неизвестного логина не удовлетворяет отличителю известного.
    /// </summary>
    private static string MaskAll(string text, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (marker.Length == 0)
            {
                continue;
            }

            text = Regex.Replace(
                text,
                Regex.Escape(marker),
                new string(' ', marker.Length),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    /// <summary>
    /// Утверждение Δkdf распознано В ПРЕДЕЛАХ ОДНОГО блока (доработка CR-002):
    /// маркер снимка/дельты счётчика KDF из <see cref="DeltaKdfMarkers"/> —
    /// глобального наличия маркера «где-то в файлах гейта» недостаточно,
    /// регрессия, убравшая утверждения Δkdf из ветки, обязана краснить ветку.
    /// </summary>
    private static bool DeltaKdfAssertedInBlock(string block) =>
        DeltaKdfMarkers.Any(marker => block.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Распознано ли в тексте гейта утверждение Δkdf=1 (снимки счётчика KDF) —
    /// ГРУБАЯ глобальная проверка всего текста; пер-веточное требование
    /// «маркер снимка в ОДНОМ блоке с веткой» enforcing'ся отдельно
    /// (<see cref="DeltaKdfAssertedInBlock"/>, доработка CR-002).
    /// Универсальные токены («before», голые слова) доказательством не
    /// считаются — только специфичные маркеры снимков/счётчика.
    /// </summary>
    public static bool DeltaKdfAsserted(string rawGateText) =>
        DeltaKdfMarkers.Any(marker =>
            StripComments(rawGateText).Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Оценка покрытия пунктов (1)–(8) по переданным исходникам LabsApp.Tests:
    /// по каждому гейту список непокрытых требований (пусто — покрыт).
    /// </summary>
    public static IReadOnlyList<GateEvaluationResult> EvaluateMandatoryGates(IReadOnlyList<string> files)
    {
        var fileCodes = new List<(string File, string Code)>();
        foreach (var file in files)
        {
            fileCodes.Add((file, StripComments(File.ReadAllText(file))));
        }

        // AnyOf-требования — глобальные по корпусу (allCode); блочные
        // требования оцениваются по ПОФАЙЛОВО построенным блокам (доработка
        // CR-002: конкатенация исходных текстов перед разбиением склеивала
        // хвост файла N с заголовком файла N+1).
        var allCode = string.Join(Environment.NewLine, fileCodes.Select(entry => entry.Code));
        var allBlocks = SplitIntoMethodBlocks(fileCodes.Select(entry => entry.Code).ToList());

        var results = new List<GateEvaluationResult>();
        foreach (var gate in MandatoryGates)
        {
            if (gate.Number == 1)
            {
                results.Add(EvaluateKdfMatrixGate(fileCodes));
                continue;
            }

            var missing = gate.Requirements
                .Where(requirement => !IsSatisfied(requirement, allCode, allBlocks))
                .Select(requirement => requirement.Description)
                .ToList();
            results.Add(new GateEvaluationResult(gate, missing));
        }

        return results;
    }

    /// <summary>
    /// Файлы, дающие сигнал покрытия хотя бы по одному требованию гейта, —
    /// база негативных проб TS-182: контрольный текст = корпус без этих
    /// файлов, и гейт обязан распознаваться как не покрытый.
    /// </summary>
    public static IReadOnlyList<string> FilesContributingTo(MandatoryGate gate, IReadOnlyList<string> files)
    {
        var contributing = new List<string>();
        foreach (var file in files)
        {
            var code = StripComments(File.ReadAllText(file));
            var blocks = SplitIntoMethodBlocks(code);
            if (gate.Requirements.Any(requirement => IsSatisfied(requirement, code, blocks)))
            {
                contributing.Add(file);
            }
        }

        return contributing;
    }

    private static bool IsSatisfied(GateRequirement requirement, string code, IReadOnlyList<string> blocks) =>
        requirement switch
        {
            AnyOfRequirement anyOf => anyOf.AnyOf.Any(token => ContainsToken(code, token)),
            BlockJointRequirement joint => blocks.Any(block =>
                joint.AnyOfA.Any(marker => ContainsToken(block, marker))
                && joint.AnyOfB.Any(marker => ContainsToken(block, marker))),
            _ => false,
        };

    private static GateEvaluationResult EvaluateKdfMatrixGate(List<(string File, string Code)> fileCodes)
    {
        var gateFiles = fileCodes
            .Where(entry => entry.Code.Contains(LoginPathMarker, StringComparison.OrdinalIgnoreCase)
                && entry.Code.Contains("kdf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (gateFiles.Count == 0)
        {
            return new GateEvaluationResult(
                MandatoryGates[0],
                [$"файлы гейта KDF-матрицы (содержащие «{LoginPathMarker}» и «kdf») в LabsApp.Tests не найдены"]);
        }

        var gateFileCodes = gateFiles.Select(entry => entry.Code).ToList();
        // Доработка CR-002: блоки строятся ПОФАЙЛОВО — конкатенация исходных
        // текстов склеивала последний тест-блок файла N с заголовком файла N+1.
        var missing = MissingKdfBranches(gateFileCodes).ToList();
        if (!DeltaKdfAsserted(string.Join(Environment.NewLine, gateFileCodes)))
        {
            missing.Insert(0, "утверждение Δkdf=1 (снимки счётчика KDF до/после запроса) не распознано");
        }

        return new GateEvaluationResult(MandatoryGates[0], missing);
    }

    /// <summary>
    /// Поиск маркера: цифровые токены — по границе слова (голая подстрока
    /// «5» в «500»/«60_000» доказательством не считается), прочие — подстрокой
    /// без учёта регистра.
    /// </summary>
    private static bool ContainsToken(string text, string token)
    {
        if (token.Length > 0 && token.All(char.IsDigit))
        {
            return Regex.IsMatch(text, $@"\b{token}\b", RegexOptions.CultureInvariant);
        }

        return text.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsStatus(string text, KdfStatus status) => status switch
    {
        KdfStatus.Ok200 => ContainsToken(text, "200")
            || text.Contains("HttpStatusCode.OK", StringComparison.Ordinal),
        KdfStatus.Unauthorized401 => ContainsToken(text, "401")
            || text.Contains("Unauthorized", StringComparison.Ordinal),
        KdfStatus.TooManyRequests429 => ContainsToken(text, "429")
            || text.Contains("TooManyRequests", StringComparison.Ordinal),
        _ => false,
    };

    private static readonly Regex ClassDeclarationRegex =
        new(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Имена классов в найденных файлах — база для --filter
    /// FullyQualifiedName~ при прогоне гейта (when TS-181).
    /// </summary>
    public static IReadOnlyList<string> ExtractTestClassNames(IReadOnlyList<string> files) =>
        files
            .SelectMany(file => ClassDeclarationRegex.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

    /// <summary>Фильтр dotnet test по классам гейта (дизъюнкция FullyQualifiedName~).</summary>
    public static string KdfGateTestFilter(IReadOnlyList<string> classNames) =>
        string.Join("|", classNames.Select(name => $"FullyQualifiedName~{name}"));
}
