namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-023 «Реестр KDF-амплитуды воспроизведён в README» (scope, FR-004 +
/// FR-028, P1).
///
/// given: файл README в каталоге src/api.
/// when:  проверка текста README.
/// then:  содержит оценку «вход: ≈50000 KDF/мин (≈833/с) устойчиво с одного IP
///        при ротации логинов» и порог алертинга auth_kdf_operations_total
///        &gt; 100/с на 5 минут (FR-004 AC «Реестр амплитуд воспроизводится в
///        README»).
/// </summary>
public sealed class Ts023_ReadmeKdfAmplitudeTests
{
    [Fact]
    public void Readme_ContainsKdfAmplitudeEstimateAndAlertThreshold()
    {
        // given: файл README в каталоге src/api.
        var readmePath = Path.Combine(FindRepoRoot(), "src", "api", "README.md");
        Assert.True(
            File.Exists(readmePath),
            $"Ожидался файл README по пути {readmePath}.");
        var content = File.ReadAllText(readmePath);

        // then: оценка амплитуды входа воспроизводится дословно (нормативная
        // формулировка FR-004 AC).
        Assert.Contains(
            "вход: ≈50000 KDF/мин (≈833/с) устойчиво с одного IP при ротации логинов",
            content);

        // then: порог алертинга auth_kdf_operations_total > 100/с на 5 минут.
        Assert.Contains("auth_kdf_operations_total", content);
        Assert.True(
            content.Contains(">100/с", StringComparison.Ordinal)
            || content.Contains("> 100/с", StringComparison.Ordinal),
            "В README отсутствует порог алертинга «auth_kdf_operations_total > 100/с».");
        Assert.Contains("5 минут", content);
    }

    /// <summary>Корень репозитория — вверх по дереву до маркерного файла.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "angular.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
