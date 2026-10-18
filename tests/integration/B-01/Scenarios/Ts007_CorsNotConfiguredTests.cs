using LabsApp.IntegrationTests.B01.Infrastructure;

namespace LabsApp.IntegrationTests.B01.Scenarios;

/// <summary>
/// Кейс TS-007 «CORS не настраивается» (FR-001, P1).
/// given: исходники src/api; запущенный тестовый хост.
/// when: статический поиск AddCors/UseCors/CorsPolicy; запрос
/// OPTIONS /api/v1/labs с заголовком Origin: http://localhost:4200.
/// then: конфигурация CORS в коде отсутствует; ответ не содержит заголовков
/// Access-Control-Allow-* (FR-001: «CORS MUST NOT настраиваться»; out_of_scope).
///
/// Примечание к объёму статического поиска: производственные исходники src/api
/// (LabsApp, включая Program.cs и Hosting/**, без bin/obj) плюс оба файла проекта
/// src/api — узкая интерпретация «по src/api»: собственный статик-гейт юнит-тестов
/// приложения (LabsApp.Tests/Hosting/HostingDemolitionGateTests.cs) содержит
/// литералы 'AddCors'/'UseCors' как запрещённые маркеры собственного списка,
/// буквальный поиск по ВСЕМ .cs-файлам src/api давал бы ложное срабатывание на
/// сам гейт (расхождение объёма поиска оформлено scenario_change_request; та же
/// техника, что у кейса TS-006 для 'EntityFrameworkCore'/'Npgsql').
/// </summary>
public sealed class Ts007_CorsNotConfiguredTests
{
    /// <summary>Запрещённые маркеры конфигурации CORS (FR-001: CORS MUST NOT настраиваться).</summary>
    private static readonly string[] CorsMarkers = { "AddCors", "UseCors", "CorsPolicy" };

    [Fact]
    public void ProductionSources_ContainNoCorsConfiguration()
    {
        // given: производственные исходники src/api/LabsApp и файлы проектов src/api.
        var files = Directory
            .EnumerateFiles(
                Path.Combine(RepoPaths.SrcApiDirectory, "LabsApp"),
                "*.cs",
                SearchOption.AllDirectories)
            .Concat(new[]
            {
                RepoPaths.LabsAppProjectPath,
                Path.Combine(RepoPaths.SrcApiDirectory, "LabsApp.Tests", "LabsApp.Tests.csproj"),
            })
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(files.Count > 0, "Производственные исходники src/api/LabsApp не найдены.");

        // when: статический поиск AddCors/UseCors/CorsPolicy.
        var violations = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                foreach (var marker in CorsMarkers)
                {
                    if (lines[lineIndex].Contains(marker, StringComparison.Ordinal))
                    {
                        violations.Add($"{file}:{lineIndex + 1}: «{marker}»");
                    }
                }
            }
        }

        // then: 0 вхождений — конфигурация CORS в коде отсутствует (FR-001).
        Assert.True(
            violations.Count == 0,
            "Найдены маркеры конфигурации CORS: " + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task OptionsLabsWithOrigin_ResponseCarriesNoAccessControlAllowHeaders()
    {
        // given: запущенный тестовый хост (Development-стенд по умолчанию).
        using var factory = new B01WebAppFactory();
        using var client = HostClients.Create(factory);
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/labs");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:4200");

        // when: OPTIONS /api/v1/labs с Origin: http://localhost:4200.
        using var response = await client.SendAsync(request);

        // then: ответ не содержит заголовков Access-Control-Allow-*.
        var corsHeaders = response.Headers
            .Where(header => header.Key.StartsWith("Access-Control-Allow", StringComparison.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}")
            .ToList();
        Assert.True(
            corsHeaders.Count == 0,
            "Ожидалось отсутствие заголовков Access-Control-Allow-* (FR-001: CORS MUST NOT "
            + "настраиваться), фактически: " + string.Join("; ", corsHeaders));
    }
}
