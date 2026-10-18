using System.Xml.Linq;

namespace LabsApp.Tests.Hosting;

/// <summary>
/// Проверка проекта хостинга по csproj (AC FR-001 «Целевая платформа», NFR-002):
/// TargetFramework = net8.0; TreatWarningsAsErrors зафиксирован; ссылки на
/// EF Core и Npgsql отсутствуют.
/// </summary>
public sealed class HostingProjectFileTests
{
    private static XDocument LoadLabsAppCsproj()
    {
        // LabsApp.Tests/bin/Debug/net8.0 → 4 уровня вверх до src/api.
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "LabsApp", "LabsApp.csproj");
        Assert.True(File.Exists(path), $"csproj не найден по пути {path}.");
        return XDocument.Load(path);
    }

    [Fact]
    public void TargetFramework_IsNet8()
    {
        var csproj = LoadLabsAppCsproj();

        var targetFramework = csproj
            .Descendants("TargetFramework")
            .Select(element => element.Value)
            .Single();

        Assert.Equal("net8.0", targetFramework);
    }

    // NFR-002: TreatWarningsAsErrors=true зафиксирован в csproj (0 предупреждений сборки).
    [Fact]
    public void TreatWarningsAsErrors_IsEnabled()
    {
        var csproj = LoadLabsAppCsproj();

        var value = csproj
            .Descendants("TreatWarningsAsErrors")
            .Select(element => element.Value.Trim())
            .Single();

        Assert.True(
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
            $"Ожидался TreatWarningsAsErrors=true, получено '{value}'.");
    }

    [Theory]
    [InlineData("EntityFramework")]
    [InlineData("Npgsql")]
    public void PackageReferences_DoNotContainForbiddenPackages(string forbidden)
    {
        var csproj = LoadLabsAppCsproj();

        var includes = csproj
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            includes,
            include => include.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }
}
