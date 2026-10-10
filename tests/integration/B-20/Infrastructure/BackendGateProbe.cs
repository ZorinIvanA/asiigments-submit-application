using System.Text;
using System.Text.RegularExpressions;

namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>Статус-маркер ветки KDF-матрицы (FR-027 гейт (1), FR-007).</summary>
public enum KdfStatus
{
    Ok200,
    Unauthorized401,
    TooManyRequests429,
}

/// <summary>
/// Ветка KDF-матрицы входа. Предикат покрытия: в ПРЕДЕЛАХ ОДНОГО
/// тест-метода/блока совместно встречаются маркер статуса (200/401/429 либо
/// HttpStatusCode.OK/Unauthorized/TooManyRequests) И отличитель ветки.
/// Одиночные универсальные токены («success», «before», «lock», голые цифры
/// без отличителя) доказательством не считаются.
/// </summary>
/// <param name="Id">Технический идентификатор ветки.</param>
/// <param name="Title">Человекочитаемое название ветки (для отчётов о провале).</param>
/// <param name="Status">Ожидаемый статус ветки.</param>
/// <param name="DistinguisherAnyOf">Отличитель ветки (достаточно одного маркера).</param>
/// <param name="DistinguisherDescription">Описание отличителя для сообщений.</param>
public sealed record KdfBranch(
    string Id,
    string Title,
    KdfStatus Status,
    IReadOnlyList<string> DistinguisherAnyOf,
    string DistinguisherDescription);

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
/// или голые цифры доказательством не считаются — цифровые маркеры ищутся по
/// границе слова.
/// </summary>
public sealed class AnyOfRequirement : GateRequirement
{
    public AnyOfRequirement(string description, IReadOnlyList<string> anyOf) : base(description) =>
        AnyOf = anyOf;

    public IReadOnlyList<string> AnyOf { get; }
}

/// <summary>
/// Требование «существует тест-метод/блок, содержащий СОВМЕСТНО маркер из
/// AnyOfA и маркер из AnyOfB» — невакуумная осмысленная комбинация.
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
/// признаку «/auth/login» + «kdf» в файле (when TS-181: маркеры
/// «POST /auth/login» и «Δkdf — снимки счётчика auth_kdf_operations_total
/// до/после»); утверждение Δkdf=1 распознаётся по специфичным маркерам
/// снимков/счётчика и проверяется отдельно (DeltaKdfAsserted). Покрытие веток
/// оценивается ПОМЕТОДНО (в пределах одного тест-метода/блока), предикаты
/// шести веток попарно различимы; у двух 429-веток отличители различны
/// (known/известн против unknown/неизвестн/ghost). Пункты (2)–(8)
/// оцениваются невакуумными комбинациями маркеров: комментарии вырезаются,
/// голые цифры/подстроки имени проекта доказательством не считаются.
/// Негативные пробы различимости выполняются на контрольных текстах
/// (см. метатесты Ts181/Ts182 зоны B-20).
/// </summary>
public static class BackendGateProbe
{
    /// <summary>Маркер пути входа POST /auth/login.</summary>
    public const string LoginPathMarker = "/auth/login";

    /// <summary>Отличитель «известный логин».</summary>
    public static IReadOnlyList<string> KnownLoginMarkers { get; } =
        ["известн", "known", "existing", "exists", "существ"];

    /// <summary>Отличитель «неизвестный логин» (дизъюнктен с KnownLoginMarkers —
    /// наборы маркеров двух 429-веток не идентичны).</summary>
    public static IReadOnlyList<string> UnknownLoginMarkers { get; } =
        ["неизвестн", "unknown", "ghost"];

    /// <summary>Отличитель «исчерпанный ключ / окно лимитера».</summary>
    public static IReadOnlyList<string> BlockedKeyMarkers { get; } =
        ["заблокир", "block", "исчерпан", "exhaust", "лимитер", "limiter", "окн", "window", "метк", "mark", "заперт"];

    /// <summary>Маркеры утверждения Δkdf=1 (снимки счётчика KDF до/после:
    /// IKdfCounter.Snapshot либо метрика auth_kdf_operations_total).</summary>
    public static IReadOnlyList<string> DeltaKdfMarkers { get; } =
    [
        "Δkdf", "deltakdf", "delta_kdf", "kdfdelta", "kdf_delta", "kdf delta",
        "счётчик kdf", "kdf counter", "kdfcounter", "kdf_counter",
        "auth_kdf_operations_total", "kdf_operations", "kdfoperations",
        "snapshot", "снимок",
    ];

    /// <summary>
    /// Шесть веток гейта KDF-матрицы (FR-027 AC «Гейт KDF-матрицы»). Предикаты
    /// попарно различимы; у двух 429-веток отличители РАЗЛИЧНЫ (известный
    /// против неизвестного логина); у ветки «успех на заблокированном ключе»
    /// маркер успеха (200) идёт совместно с маркером исчерпанного ключа/окна
    /// лимитера.
    /// </summary>
    public static IReadOnlyList<KdfBranch> KdfMatrixBranches { get; } =
    [
        new("200-known-correct",
            "200 (известный логин + верный пароль)",
            KdfStatus.Ok200, KnownLoginMarkers, "известный логин"),
        new("401-known-wrong",
            "401 (известный логин + неверный пароль)",
            KdfStatus.Unauthorized401, KnownLoginMarkers, "известный логин"),
        new("401-unknown",
            "401 (неизвестный логин)",
            KdfStatus.Unauthorized401, UnknownLoginMarkers, "неизвестный логин"),
        new("429-known",
            "429 (известный логин, заблокированный ключ)",
            KdfStatus.TooManyRequests429, KnownLoginMarkers, "известный логин"),
        new("429-unknown",
            "429 (неизвестный логин, заблокированный ключ)",
            KdfStatus.TooManyRequests429, UnknownLoginMarkers, "неизвестный логин"),
        new("200-blocked-key",
            "200 (успех на заблокированном ключе)",
            KdfStatus.Ok200, BlockedKeyMarkers, "исчерпанный ключ/окно лимитера"),
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
    /// и упоминание KDF/Δkdf (гейт (1) FR-027).
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
    /// Делит код (уже без комментариев) на блоки тест-методов по атрибутам
    /// [Fact]/[Theory]. Содержимое до первого атрибута (usings, поля класса,
    /// хелперы) блоком НЕ считается — маркер «где-то в файле» доказательством
    /// ветки не является: покрытие оценивается в пределах одного блока.
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

    private static readonly Regex TestMethodAttributeRegex =
        new(@"^\s*\[(Fact|Theory)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Список НЕ покрытых веток KDF-матрицы для текста гейта: ветка считается
    /// покрытой, только если в одном блоке (тест-методе) совместно найдены
    /// маркер статуса и отличитель ветки.
    /// </summary>
    public static IReadOnlyList<string> MissingKdfBranches(string rawGateText)
    {
        var blocks = SplitIntoMethodBlocks(StripComments(rawGateText));
        return KdfMatrixBranches
            .Where(branch => !blocks.Any(block => BranchCoveredInBlock(branch, block)))
            .Select(branch => branch.Title)
            .ToList();
    }

    private static bool BranchCoveredInBlock(KdfBranch branch, string block) =>
        ContainsStatus(block, branch.Status)
        && branch.DistinguisherAnyOf.Any(marker => ContainsToken(block, marker));

    /// <summary>
    /// Распознано ли в тексте гейта утверждение Δkdf=1 (снимки счётчика KDF).
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

        var allCode = string.Join(Environment.NewLine, fileCodes.Select(entry => entry.Code));
        var allBlocks = SplitIntoMethodBlocks(allCode);

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

        var gateText = string.Join(Environment.NewLine, gateFiles.Select(entry => entry.Code));
        var missing = MissingKdfBranches(gateText).ToList();
        if (!DeltaKdfAsserted(gateText))
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
