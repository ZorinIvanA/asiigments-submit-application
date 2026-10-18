using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// Кейс батча B-19: TS-172 «Клиент: контракты сервисов и моделей сохранены» —
/// реализован этим классом (исторические ID: кейс раунда c-882 TS-143;
/// сценарий контура TS-178).
/// TS-178 «Клиент: публичный контракт сервисов core не изменился» (nfr, P1,
/// FR-026 AC «Сервисы не изменили контракт», контракт IF-014/IF-018).
/// given: дерево src/client/app/core/services/*.ts и
///        src/client/app/shared/models.ts;
/// when:  инспекция: методы и типы публичного API; поиск API_BASE_URL и
///        HttpClient-вызовов; проверка экспортов models.ts;
/// then:  методы и типы (LabsService.getList и пр.) неизменны относительно
///        эталона ветки; HTTP-вызовы идут через API_BASE_URL='/api/v1';
///        интерцептор авторизации и ApiError-нормализация сохранены;
///        models.ts экспортирует MeDto, ProfileDto, LabDto, GroupDto,
///        StudentDto, SubmissionsGridDto, MySubmissionsDto, PagedResult,
///        Submission, STORAGE_KEYS.recoveryFlow, ApiError
///        (FR-026 AC «Сервисы не изменили контракт»).
/// </summary>
public sealed class Ts178_CoreServicesContractTests
{
    /// <summary>Сервисы core доменов — проверяемые файлы (относительные пути).</summary>
    private static readonly string[] CoreServiceRelativePaths =
    [
        "src/client/app/core/services/auth.service.ts",
        "src/client/app/core/services/groups.service.ts",
        "src/client/app/core/services/labs.service.ts",
        "src/client/app/core/services/profile.service.ts",
        "src/client/app/core/services/students.service.ts",
        "src/client/app/core/services/submissions.service.ts",
    ];

    /// <summary>
    /// Начало объявления метода класса на отступе 2 пробела (стиль сервиса):
    /// имя + необязательный generic + '(' — с/без модификатора async.
    /// </summary>
    private static readonly Regex MethodDeclarationStart = new(
        @"^  (?:async\s+)?(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?<generics><[^>(]*>)?\(",
        RegexOptions.Compiled);

    private static readonly HashSet<string> NonMethodNames = new(StringComparer.Ordinal)
    {
        "constructor", "if", "for", "while", "switch", "catch", "return", "function",
    };

    /// <summary>
    /// then: методы и типы публичного API неизменны относительно эталона ветки.
    /// Эталон — версия файлов в HEAD текущей ветки (git show HEAD:path); из
    /// каждой версии извлекаются публичные сигнатуры методов класса (первая
    /// строка объявления + продолжение до '{'/'-', нормализация пробелов).
    /// Требование: каждая сигнатура эталона присутствует в текущей версии
    /// (расширение API новой операцией миграции не является изменением
    /// существующего контракта; удаление/переименование/смена сигнатуры — является).
    /// </summary>
    [Fact]
    public void PublicServiceMethods_AreUnchangedAgainstBranchReference()
    {
        var violations = new List<string>();

        foreach (var relativePath in CoreServiceRelativePaths)
        {
            var reference = GitCli.ShowHeadBlob(relativePath);
            var currentFile = Path.Combine(RepoPaths.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(currentFile))
            {
                violations.Add($"{relativePath}: файл удалён, а эталонный контракт существует в HEAD.");
                continue;
            }

            var referenceSignatures = ExtractPublicMethodSignatures(reference);
            var currentSignatures = ExtractPublicMethodSignatures(File.ReadAllText(currentFile));

            foreach (var signature in referenceSignatures)
            {
                if (!currentSignatures.Contains(signature))
                {
                    violations.Add(
                        $"{relativePath}: сигнатура из эталона HEAD не найдена (изменена/удалена): {signature}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: публичный контракт сервисов core изменился относительно эталона ветки:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then: HTTP-вызовы идут через API_BASE_URL='/api/v1'. Каждый сервис core
    /// инжектирует HttpClient и токен API_BASE_URL (единственный котируемый
    /// литерал префикса — фабрика core/api-base-url.ts, IF-014 input).
    /// </summary>
    [Fact]
    public void Services_RouteHttpCallsThroughApiBaseUrl()
    {
        var violations = new List<string>();

        foreach (var relativePath in CoreServiceRelativePaths)
        {
            var file = Path.Combine(RepoPaths.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
            {
                violations.Add($"{relativePath}: файл отсутствует.");
                continue;
            }

            var content = File.ReadAllText(file);
            if (!content.Contains("inject(HttpClient)", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: нет inject(HttpClient) — сервис не на реальных HTTP-вызовах.");
            }

            if (!content.Contains("inject(API_BASE_URL)", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: нет inject(API_BASE_URL) — вызовы идут мимо токена префикса.");
            }

            if (!content.Contains("from '../api-base-url'", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: нет импорта core/api-base-url.");
            }
        }

        var apiBaseUrlSource = File.ReadAllText(RepoPaths.ApiBaseUrlFile);
        if (!apiBaseUrlSource.Contains("'/api/v1'", StringComparison.Ordinal))
        {
            violations.Add("core/api-base-url.ts: фабрика токена не содержит литерал '/api/v1'.");
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: HTTP-вызовы сервисов core не идут через API_BASE_URL='/api/v1':"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then: интерцептор авторизации и ApiError-нормализация сохранены
    /// (IF-014): provideHttpClient(withInterceptors([authInterceptor])) в
    /// app.config.ts; в интерцепторе — withCredentials на API-запросах и
    /// дедуплицируемый 401 → POST /auth/refresh; в http-errors.ts —
    /// нормализация отказов в ApiError {status, body:{message, errors?}}.
    /// </summary>
    [Fact]
    public void AuthInterceptorAndApiErrorNormalization_ArePreserved()
    {
        var violations = new List<string>();

        var appConfig = File.ReadAllText(RepoPaths.AppConfigFile);
        if (!appConfig.Contains("withInterceptors([authInterceptor])", StringComparison.Ordinal))
        {
            violations.Add("app.config.ts: provideHttpClient без withInterceptors([authInterceptor]).");
        }

        var interceptor = File.ReadAllText(RepoPaths.AuthInterceptorFile);
        if (!interceptor.Contains("withCredentials", StringComparison.Ordinal))
        {
            violations.Add("core/auth-interceptor.ts: не выставляется withCredentials на API-запросах.");
        }

        if (!interceptor.Contains("/auth/refresh", StringComparison.Ordinal))
        {
            violations.Add("core/auth-interceptor.ts: нет ретрафа через POST /auth/refresh.");
        }

        var httpErrors = File.ReadAllText(RepoPaths.HttpErrorsFile);
        if (!httpErrors.Contains("ApiError", StringComparison.Ordinal))
        {
            violations.Add("core/http-errors.ts: нет ApiError-нормализации.");
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: интерцептор авторизации / ApiError-нормализация утрачены:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then (скобка кейса TS-172): models.ts экспортирует типы контракта —
    /// MeDto, ProfileDto, LabDto, GroupDto, StudentDto, SubmissionsGridDto,
    /// MySubmissionsDto, PagedResult, Submission, ApiError — и ключ
    /// STORAGE_KEYS.recoveryFlow (FR-026 AC «Сервисы не изменили контракт»).
    /// </summary>
    [Fact]
    public void SharedModels_ExportContractTypesAndRecoveryFlowKey()
    {
        Assert.True(
            File.Exists(RepoPaths.SharedModelsFile),
            $"given не выполнен: не найден {RepoPaths.SharedModelsFile}.");

        var content = File.ReadAllText(RepoPaths.SharedModelsFile);
        var violations = new List<string>();

        foreach (var typeName in ContractTypeNames)
        {
            if (!Regex.IsMatch(content, $@"export\s+(?:interface|type|class)\s+{typeName}\b"))
            {
                violations.Add($"экспорт типа {typeName} отсутствует.");
            }
        }

        var storageKeysBlock = ExtractStorageKeysBlock(content);
        if (!storageKeysBlock.Contains("recoveryFlow", StringComparison.Ordinal))
        {
            violations.Add("ключ STORAGE_KEYS.recoveryFlow отсутствует.");
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: shared/models.ts не экспортирует типы контракта:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>Типы контракта, обязанные остаться в shared/models.ts (TS-172).</summary>
    private static readonly string[] ContractTypeNames =
    [
        "MeDto", "ProfileDto", "LabDto", "GroupDto", "StudentDto",
        "SubmissionsGridDto", "MySubmissionsDto", "PagedResult", "Submission", "ApiError",
    ];

    /// <summary>Блок объектного литерала STORAGE_KEYS (между '{' и '}').</summary>
    private static string ExtractStorageKeysBlock(string content)
    {
        var declarationStart = content.IndexOf("export const STORAGE_KEYS", StringComparison.Ordinal);
        if (declarationStart < 0)
        {
            return string.Empty;
        }

        var openBrace = content.IndexOf('{', declarationStart);
        var closeBrace = content.IndexOf('}', openBrace);
        return openBrace < 0 || closeBrace < 0
            ? string.Empty
            : content[(openBrace + 1)..closeBrace];
    }

    /// <summary>
    /// Извлечь нормализованные публичные сигнатуры методов класса: строка
    /// объявления накапливается до завершения ('{' или ';' в конце строки),
    /// пробелы схлопываются; конструктор и приватные члены не входят.
    /// </summary>
    private static IReadOnlySet<string> ExtractPublicMethodSignatures(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var match = MethodDeclarationStart.Match(lines[index]);
            if (!match.Success || NonMethodNames.Contains(match.Groups["name"].Value))
            {
                continue;
            }

            var signatureLines = new List<string> { lines[index] };
            while (!IsSignatureComplete(signatureLines[^1]) && index + 1 < lines.Length)
            {
                signatureLines.Add(lines[++index]);
            }

            var normalized = Regex
                .Replace(string.Join(" ", signatureLines), @"\s+", " ")
                .TrimEnd()
                .TrimEnd('{')
                .TrimEnd();
            signatures.Add(normalized);
        }

        return signatures;
    }

    private static bool IsSignatureComplete(string line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.EndsWith('{') || trimmed.EndsWith(';');
    }
}
