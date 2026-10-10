using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// Кейс батча B-19: TS-173 «Клиент: mock-только экспорты удалены,
/// recoveryFlow/ApiError сохранены» — реализован этим классом (исторические
/// ID: кейс раунда c-882 TS-145; сценарий контура TS-179).
/// TS-179 «Клиент: mock-только экспорты models удалены, нужные сохранены»
/// (scope, P1, FR-026 п.4).
/// given: src/client/app/shared/models.ts после миграции;
/// when:  проверка экспортов STORAGE_KEYS и ApiError;
/// then:  STORAGE_KEYS.mockDb, STORAGE_KEYS.session, MOCK_DELAY_MS отсутствуют
///        (вместе с их тестами); STORAGE_KEYS.recoveryFlow и ApiError сохранены
///        (FR-026 п.4).
/// </summary>
public sealed class Ts179_ModelsMockExportsTests
{
    private static readonly string[] RemovedExportMarkers =
    [
        "STORAGE_KEYS.mockDb",
        "STORAGE_KEYS.session",
        "MOCK_DELAY_MS",
    ];

    /// <summary>
    /// when/then: в shared/models.ts нет ключей mockDb и session в STORAGE_KEYS,
    /// нет константы MOCK_DELAY_MS; ключ recoveryFlow и экспорт ApiError
    /// сохранены (FR-026 п.4: «STORAGE_KEYS.recoveryFlow и ApiError сохраняются»).
    /// </summary>
    [Fact]
    public void ModelsFile_RemovedMockExportsAbsent_NeededOnesKept()
    {
        Assert.True(
            File.Exists(RepoPaths.SharedModelsFile),
            $"given не выполнен: не найден {RelativePath(RepoPaths.SharedModelsFile)}.");

        var content = File.ReadAllText(RepoPaths.SharedModelsFile);
        var violations = new List<string>();

        var storageKeys = ExtractStorageKeyNames(content);
        if (storageKeys.Contains("mockDb"))
        {
            violations.Add("STORAGE_KEYS.mockDb всё ещё объявлен.");
        }

        if (storageKeys.Contains("session"))
        {
            violations.Add("STORAGE_KEYS.session всё ещё объявлен.");
        }

        if (!storageKeys.Contains("recoveryFlow"))
        {
            violations.Add("STORAGE_KEYS.recoveryFlow отсутствует — обязан быть сохранён (FR-026 п.4).");
        }

        if (content.Contains("MOCK_DELAY_MS", StringComparison.Ordinal))
        {
            violations.Add("MOCK_DELAY_MS всё ещё присутствует в файле.");
        }

        if (!Regex.IsMatch(content, @"export\s+(?:interface|type|class)\s+ApiError\b"))
        {
            violations.Add("экспорт ApiError отсутствует — обязан быть сохранён (FR-026 п.4).");
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: экспорты shared/models.ts не соответствуют FR-026 п.4:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then (скобка кейса): mock-только экспорты удалены «вместе с их тестами» —
    /// ни один *.spec.ts под src/client не ссылается на STORAGE_KEYS.mockDb,
    /// STORAGE_KEYS.session или MOCK_DELAY_MS.
    /// </summary>
    [Fact]
    public void SpecFiles_HaveNoReferencesToRemovedExports()
    {
        var violations = new List<string>();

        if (!Directory.Exists(RepoPaths.ClientSrcDirectory))
        {
            Assert.Fail($"given не выполнен: нет каталога {RepoPaths.ClientSrcDirectory}.");
        }

        foreach (var specFile in Directory.EnumerateFiles(
            RepoPaths.ClientSrcDirectory, "*.spec.ts", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(specFile);
            foreach (var marker in RemovedExportMarkers)
            {
                var offset = 0;
                while (true)
                {
                    var found = content.IndexOf(marker, offset, StringComparison.Ordinal);
                    if (found < 0)
                    {
                        break;
                    }

                    var line = content[..found].Count(static ch => ch == '\n') + 1;
                    violations.Add($"{RelativePath(specFile)}:{line}: '{marker}'");
                    offset = found + marker.Length;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "then не выполнен: spec-файлы всё ещё тестируют удалённые mock-экспорты:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// Имена ключей в объектном литерале STORAGE_KEYS (блок между
    /// «export const STORAGE_KEYS = {» и закрывающей '}').
    /// </summary>
    private static IReadOnlySet<string> ExtractStorageKeyNames(string content)
    {
        var declarationStart = content.IndexOf("export const STORAGE_KEYS", StringComparison.Ordinal);
        if (declarationStart < 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var openBrace = content.IndexOf('{', declarationStart);
        var closeBrace = content.IndexOf('}', openBrace);
        if (openBrace < 0 || closeBrace < 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var block = content[(openBrace + 1)..closeBrace];
        return new HashSet<string>(
            Regex.Matches(block, @"(?m)^\s*(?<key>[A-Za-z_$][A-Za-z0-9_$]*)\s*:").Select(match => match.Groups["key"].Value),
            StringComparer.Ordinal);
    }

    private static string RelativePath(string absolutePath) =>
        Path.GetRelativePath(RepoPaths.RepositoryRoot, absolutePath);
}
