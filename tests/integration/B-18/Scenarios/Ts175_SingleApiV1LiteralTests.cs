using LabsApp.IntegrationTests.B18.Infrastructure;

namespace LabsApp.IntegrationTests.B18.Scenarios;

/// <summary>
/// TS-175 «Единый литерал префикса /api/v1 в клиенте» (P1, FR-091,
/// AC «Единый префикс», гейт QG-004).
/// given: исходники src/client;
/// when:  grep литерала '/api/v1' по всем исходникам src/client;
/// then:  ровно одно вхождение — фабрика API_BASE_URL в файле
///        api-base-url.ts; вне этого файла вхождений ноль.
/// Проверка статическая и детерминированная: сканируются текстовые исходники
/// клиента (*.ts, *.js, *.html, *.scss, *.css, *.json) рекурсивно.
/// </summary>
public sealed class Ts175_SingleApiV1LiteralTests
{
    private const string ApiV1Literal = "/api/v1";
    private const string AllowedFileName = "api-base-url.ts";

    private static readonly string[] SourceExtensions =
    [
        ".ts",
        ".js",
        ".html",
        ".scss",
        ".css",
        ".json",
    ];

    [Fact]
    public void ApiV1Literal_SingleOccurrence_InApiBaseUrlFactory()
    {
        // when: grep литерала '/api/v1' по всем исходникам src/client.
        var violations = new List<string>();
        var allowedFileHits = 0;

        foreach (var file in EnumerateClientSources())
        {
            var content = File.ReadAllText(file);
            var occurrences = CountOccurrences(content, ApiV1Literal);
            if (occurrences == 0)
            {
                continue;
            }

            if (Path.GetFileName(file) == AllowedFileName)
            {
                allowedFileHits += occurrences;
                continue;
            }

            var lineNumber = 1 + content.AsSpan(0, content.IndexOf(ApiV1Literal, StringComparison.Ordinal))
                .Count('\n');
            violations.Add($"{RelativePath(file)}:{lineNumber}: {occurrences} вхожд.");
        }

        // then: ровно одно вхождение — фабрика API_BASE_URL в api-base-url.ts;
        // вне этого файла вхождений ноль.
        Assert.True(
            violations.Count == 0,
            $"Литерал '{ApiV1Literal}' найден вне {AllowedFileName} (допустимо единственное вхождение — фабрика API_BASE_URL, FR-091/QG-004):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(item => $"  - {item}")));
        Assert.True(
            allowedFileHits == 1,
            $"В {AllowedFileName} ожидается ровно одно вхождение '{ApiV1Literal}' (фабрика API_BASE_URL), фактически: {allowedFileHits}.");
    }

    private static IEnumerable<string> EnumerateClientSources()
    {
        if (!Directory.Exists(RepoPaths.ClientSrcDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(
                RepoPaths.ClientSrcDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(file => SourceExtensions.Contains(Path.GetExtension(file)));
    }

    private static int CountOccurrences(string content, string pattern)
    {
        var count = 0;
        var index = content.IndexOf(pattern, StringComparison.Ordinal);
        while (index >= 0)
        {
            count += 1;
            index = content.IndexOf(pattern, index + pattern.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string RelativePath(string file)
    {
        return Path.GetRelativePath(RepoPaths.RepositoryRoot, file);
    }
}
