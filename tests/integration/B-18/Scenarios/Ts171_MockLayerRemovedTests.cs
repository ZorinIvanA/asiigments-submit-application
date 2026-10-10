using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// TS-171 «Мок-слой клиента удалён целиком» (P0, FR-090, AC «Каталог удалён»).
/// given: рабочее дерево ветки lab3-api;
/// when:  проверка ФС — существование src/client/app/mock; grep 'app/mock'
///        по src/client/**/*.ts; grep 'mock.db.v1' и 'mock.session.userId'
///        по src/client;
/// then:  каталог src/client/app/mock не существует; импортов из app/mock
///        нет; localStorage-ключи мока нигде не читаются и не пишутся;
///        константа recovery.flow.v1 сохранена; out_of_scope подтверждается
///        отсутствием лишнего кода.
/// Проверка статическая и детерминированная — только файловая система и
/// содержимое исходников, без сборки и сети.
/// </summary>
public sealed class Ts171_MockLayerRemovedTests
{
    private const string AppMockFragment = "app/mock";
    private const string MockDbKey = "mock.db.v1";
    private const string MockSessionKey = "mock.session.userId";
    private const string RecoveryFlowKey = "recovery.flow.v1";

    /// <summary>then: «Каталог src/client/app/mock не существует».</summary>
    [Fact]
    public void MockCatalog_DoesNotExist()
    {
        Assert.False(
            Directory.Exists(RepoPaths.ClientMockDirectory),
            $"Каталог мок-слоя {RepoPaths.ClientMockDirectory} должен быть удалён целиком (FR-090).");
    }

    /// <summary>
    /// when: grep 'app/mock' по src/client/**/*.ts;
    /// then: «импортов из app/mock нет» — подстрока 'app/mock' не встречается
    /// ни в одном *.ts под src/client.
    /// </summary>
    [Fact]
    public void NoAppMock_ReferencesInClientTypeScript()
    {
        var violations = new List<string>();
        foreach (var file in EnumerateClientFiles("*.ts"))
        {
            foreach ((var lineNumber, var line) in EnumerateLines(file))
            {
                if (line.Contains(AppMockFragment, StringComparison.Ordinal))
                {
                    violations.Add($"{RelativePath(file)}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "В src/client/**/*.ts найдены вхождения 'app/mock' (импорты мок-слоя обязаны отсутствовать, FR-090):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// when: grep 'mock.db.v1' и 'mock.session.userId' по src/client;
    /// then: «localStorage-ключи мока нигде не читаются и не пишутся».
    /// </summary>
    [Fact]
    public void MockLocalStorageKeys_AreNotReadOrWritten()
    {
        var violations = new List<string>();
        foreach (var file in EnumerateClientFiles("*"))
        {
            foreach ((var lineNumber, var line) in EnumerateLines(file))
            {
                if (line.Contains(MockDbKey, StringComparison.Ordinal))
                {
                    violations.Add($"{RelativePath(file)}:{lineNumber}: {MockDbKey}: {line.Trim()}");
                }

                if (line.Contains(MockSessionKey, StringComparison.Ordinal))
                {
                    violations.Add($"{RelativePath(file)}:{lineNumber}: {MockSessionKey}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "В src/client найдены localStorage-ключи мока ('mock.db.v1'/'mock.session.userId') — они не читаются и не пишутся (FR-090/FR-092):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
    }

    /// <summary>
    /// then: «константа recovery.flow.v1 сохранена» — ключ потока
    /// восстановления присутствует в исходниках клиента.
    /// </summary>
    [Fact]
    public void RecoveryFlowKey_IsPreserved()
    {
        var preserved = EnumerateClientFiles("*")
            .Any(file => File.ReadAllText(file).Contains(RecoveryFlowKey, StringComparison.Ordinal));

        Assert.True(
            preserved,
            $"Константа {RecoveryFlowKey} не найдена в src/client — ключ потока восстановления должен быть сохранён (FR-090).");
    }

    private static IEnumerable<string> EnumerateClientFiles(string pattern)
    {
        if (!Directory.Exists(RepoPaths.ClientSrcDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(
            RepoPaths.ClientSrcDirectory,
            pattern,
            SearchOption.AllDirectories);
    }

    private static IEnumerable<(int LineNumber, string Line)> EnumerateLines(string file)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(file))
        {
            lineNumber += 1;
            yield return (lineNumber, line);
        }
    }

    private static string RelativePath(string file)
    {
        return Path.GetRelativePath(RepoPaths.RepositoryRoot, file);
    }
}
