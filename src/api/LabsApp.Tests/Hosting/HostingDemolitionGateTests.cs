using System.Reflection;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Статические гейты демонтажа конвейера v2.2 (C-001): 413-гейт приложения удалён
/// целиком — граница 413 принадлежит Kestrel (BUG-004, ISS-016/OQ-004), CORS не
/// настраивается вовсе (FR-001), обработка X-Forwarded-For удалена — IP клиента =
/// Connection.RemoteIpAddress (ASM-013/ADR-006), фиксированный dev-ключ JWT не
/// существует — только эпизодический случайный (FR-006). Источник сканируется
/// пофайлово: Program.cs и Hosting/** — зона композиция-корня и его middleware;
/// маркеры — имена API/констант, не встречающиеся в поясняющих комментариях.
/// Словарный текст для 413 не вводился: конверт хостинга содержит ровно три
/// документированных текста (IF-001/FR-023).
/// </summary>
public sealed class HostingDemolitionGateTests
{
    // Маркеры демонтированной инфраструктуры v2.3: любое вхождение в исходниках
    // зоны хостинга — регресс (возвращённый 413-гейт, CORS, ForwardedHeaders,
    // константный dev-ключ).
    private static readonly string[] ForbiddenMarkers =
    [
        "RequestSizeGate",
        "AddCors",
        "UseCors",
        "DevelopmentCors",
        "WithOrigins",
        "UseForwardedHeaders",
        "ForwardedProxyOptions",
        "KnownProxies",
        "DevelopmentJwtKey",
    ];

    private static string LabsAppDirectory()
    {
        // LabsApp.Tests/bin/Debug/net8.0 → 4 уровня вверх до src/api, затем LabsApp.
        // GetFullPath обязателен: EnumerateFiles сохраняет сегменты '..' в путях
        // результатов, и не-канонический путь ломал бы фильтры по '/bin/'-сегментам.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "LabsApp"));
        Assert.True(Directory.Exists(path), $"Каталог LabsApp не найден по пути {path}.");
        return path;
    }

    private static IReadOnlyList<string> HostingSourceFiles()
    {
        var labsApp = LabsAppDirectory();
        var program = Path.Combine(labsApp, "Program.cs");
        Assert.True(File.Exists(program), $"Program.cs не найден по пути {program}.");

        var hosting = Path.Combine(labsApp, "Hosting");
        Assert.True(Directory.Exists(hosting), $"Каталог Hosting не найден по пути {hosting}.");

        return Directory
            .EnumerateFiles(hosting, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Append(program)
            .ToList();
    }

    [Fact]
    public void HostingSources_DoNotContainDemolishedInfrastructureMarkers()
    {
        var files = HostingSourceFiles();

        // Защита от пустого скана (ошибочный путь дал бы ложноположительный успех).
        Assert.True(
            files.Count > 1,
            $"Ожидались файлы Hosting/** и Program.cs, найдено {files.Count}. "
            + $"Base={AppContext.BaseDirectory}; LabsApp={LabsAppDirectory()}; "
            + $"HostDirExists={Directory.Exists(Path.Combine(LabsAppDirectory(), "Hosting"))}; "
            + $"NoPatternCount={Directory.EnumerateFiles(Path.Combine(LabsAppDirectory(), "Hosting"), "*", SearchOption.AllDirectories).Count()}.");

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var marker in ForbiddenMarkers)
            {
                Assert.True(
                    !source.Contains(marker, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} содержит демонтированный маркер '{marker}' (BUG-004/FR-001/ASM-013/FR-006).");
            }
        }
    }

    // AC BUG-004: словарный текст для 413 не вводился — конверт хостинга отвечает
    // только текстами 400/404/500 (тела 413/невалидного HTTP — фреймворк до конвейера).
    [Fact]
    public void HostingErrorTexts_ContainOnlyDocumentedTexts_No413Text()
    {
        var fields = typeof(ErrorTexts)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (Name: field.Name, Value: (string)field.GetRawConstantValue()!))
            .ToList();

        Assert.Equal(
            new[] { nameof(ErrorTexts.InvalidData), nameof(ErrorTexts.NotFound), nameof(ErrorTexts.InternalServerError) },
            fields.Select(field => field.Name));

        Assert.All(fields, field =>
        {
            Assert.DoesNotContain("413", field.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("большое", field.Value, StringComparison.OrdinalIgnoreCase);
        });
    }
}
