namespace LabsApp.IntegrationTests.B20.Infrastructure;

/// <summary>
/// Пути к артефактам репозитория для ворот TS-149/NFR-002 (и ng-прогонов),
/// не вошедшие в <see cref="RepoPaths"/>/‌<see cref="BackendSuitePaths"/>
/// (собственные файлы чужих сценариев зоны B-20 не редактируются).
/// </summary>
public static class NfrGatePaths
{
    /// <summary>Csproj проекта реализации — фиксация TreatWarningsAsErrors (TS-149).</summary>
    public static string ApiProjectFile => Path.Combine(
        RepoPaths.RepositoryRoot, "src", "api", "LabsApp", "LabsApp.csproj");

    /// <summary>Csproj проекта тестов реализации — фиксация TreatWarningsAsErrors (TS-149).</summary>
    public static string ApiTestsProjectFile => BackendSuitePaths.TestsProjectFile;

    /// <summary>Решение бэкенда src/api — dotnet build ворот TS-149 (NFR-002).</summary>
    public static string ApiSolutionFile => BackendSuitePaths.SolutionFile;

    /// <summary>CLI Angular: node_modules/@angular/cli/bin/ng.js.</summary>
    public static string NgCliJs =>
        Path.Combine(RepoPaths.RepositoryRoot, "node_modules", "@angular", "cli", "bin", "ng.js");
}
