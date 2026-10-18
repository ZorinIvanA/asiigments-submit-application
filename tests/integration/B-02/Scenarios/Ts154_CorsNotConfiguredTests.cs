using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B02.Infrastructure;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-154 «Scope: CORS не настраивается» (FR-001 «CORS MUST NOT настраиваться»,
/// out_of_scope, P2).
///
/// given: хост запущен; исходники src/api/LabsApp (Program.cs) доступны.
/// when:  GET /api/v1/labs с заголовком Origin: http://evil.example; поиск
///        AddCors/UseCors в исходниках src/api/LabsApp.
/// then:  ответ не содержит заголовков Access-Control-Allow-*; вызовов AddCors/UseCors
///        нет (ни регистрации сервиса CORS, ни middleware в конвейере).
///
/// Греп исходников — по шаблону ВЫЗОВА (\b(AddCors|UseCors)\s*\() по всем .cs файлам
/// src/api/LabsApp (без bin/obj): комментарий Program.cs «CORS ... НЕ настраиваются»
/// вызовом не является и совпадением не считается.
/// </summary>
public sealed class Ts154_CorsNotConfiguredTests : IClassFixture<B02WebAppFactory>
{
    private static readonly Regex CorsCallPattern = new(
        @"\b(AddCors|UseCors)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly B02WebAppFactory _factory;

    public Ts154_CorsNotConfiguredTests(B02WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetLabsWithForeignOrigin_ResponseCarriesNoAccessControlAllowHeaders()
    {
        using var client = _factory.CreateWarmClient();

        // when: GET /api/v1/labs с чужим Origin (статус не важен — инспектируются заголовки).
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/labs");
        request.Headers.TryAddWithoutValidation("Origin", "http://evil.example");
        using var response = await client.SendAsync(request);

        // then: ни одного заголовка Access-Control-Allow-* (ни в заголовках ответа,
        // ни в заголовках содержимого).
        var corsHeaders = response.Headers
            .Concat(response.Content.Headers)
            .Select(header => header.Key)
            .Where(name => name.StartsWith("Access-Control-Allow-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(
            corsHeaders.Count == 0,
            $"Ответ содержит CORS-заголовки ({string.Join(", ", corsHeaders)}) — CORS настроен, "
            + "хотя FR-001 требует обратного.");
    }

    [Fact]
    public void LabsAppSources_DoNotCallAddCorsOrUseCors()
    {
        // when: поиск вызовов AddCors/UseCors по .cs исходникам src/api/LabsApp
        // (каталоги сборки bin/obj исключаются).
        var sourceFiles = Directory.EnumerateFiles(
                RepoPaths.LabsAppSourceDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
                    && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
            })
            .ToList();

        Assert.True(
            sourceFiles.Count > 0,
            $"Исходники .cs не найдены в {RepoPaths.LabsAppSourceDirectory} — given кейса нарушен.");

        var offenders = new List<string>();
        foreach (var path in sourceFiles)
        {
            var content = File.ReadAllText(path);
            if (CorsCallPattern.IsMatch(content))
            {
                offenders.Add(Path.GetRelativePath(RepoPaths.RepositoryRoot, path));
            }
        }

        // then: вызовов AddCors/UseCors нет.
        Assert.True(
            offenders.Count == 0,
            $"Найдены вызовы AddCors/UseCors (CORS настраиваться не должен, FR-001): {string.Join(", ", offenders)}.");
    }
}
