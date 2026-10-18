using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-176 «Бэкенд: TreatWarningsAsErrors зафиксирован в csproj»
/// (NFR-002, P2).
/// given: Файл src/api/LabsApp/LabsApp.csproj.
/// when:  Чтение csproj.
/// then:  Содержит TreatWarningsAsErrors=true (NFR-002: «фиксация
///        TreatWarningsAsErrors в csproj»; dotnet build завершается с 0
///        предупреждений).
/// </summary>
public sealed class Ts176_Nfr002TreatWarningsAsErrorsTests
{
    [Fact]
    public void LabsAppCsproj_ContainsTreatWarningsAsErrorsTrue()
    {
        // given: файл src/api/LabsApp/LabsApp.csproj.
        var csprojPath = Path.Combine(
            RepoPaths.RepositoryRoot, "src", "api", "LabsApp", "LabsApp.csproj");
        Assert.True(
            File.Exists(csprojPath),
            $"given не выполнен: не найден файл {csprojPath}.");

        // when: чтение csproj.
        var content = File.ReadAllText(csprojPath);

        // then: содержит TreatWarningsAsErrors=true (NFR-002: «фиксация
        // TreatWarningsAsErrors в csproj»).
        Assert.True(
            Regex.IsMatch(
                content,
                @"<TreatWarningsAsErrors>\s*true\s*</TreatWarningsAsErrors>",
                RegexOptions.IgnoreCase),
            "then не выполнен: src/api/LabsApp/LabsApp.csproj не содержит "
            + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors> "
            + "(NFR-002: «фиксация TreatWarningsAsErrors в csproj»).");
    }
}
