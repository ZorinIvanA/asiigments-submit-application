using System.Text.RegularExpressions;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-021 «Реестр KDF-амплитуды воспроизведён в README» (scope, FR-004, FR-028, P1).
///
/// given: README каталога src/api после реализации.
/// when:  проверка текста README.
/// then:  содержит оценку «вход: ≈50000 KDF/мин (≈833/с) устойчиво с одного IP
///        при ротации логинов» и порог алертинга auth_kdf_operations_total > 100/с
///        на 5 минут (FR-004 AC «Реестр амплитуд воспроизводится в README»; FR-028 п.4).
/// </summary>
public sealed partial class Ts021_ReadmeKdfAmplitudeRegistryTests
{
    [Fact]
    public void Readme_ReproducesKdfAmplitudeEstimateAndAlertThreshold()
    {
        // given/when: текст README каталога src/api.
        var readmePath = Path.Combine(B09RepoPaths.SrcApiDirectory, "README.md");
        Assert.True(
            File.Exists(readmePath),
            $"README каталога src/api не найден: {readmePath} (FR-028 обязывает его существовать).");
        var readme = File.ReadAllText(readmePath);

        // then: оценка амплитуды входа (нагрузочные фрагменты кейса дословно).
        AssertFragment(readme, "50000 KDF/мин", "оценка амплитуды входа ≈50000 KDF/мин");
        AssertFragment(readme, "833/с", "эквивалент ≈833 KDF/с");
        AssertFragment(readme, "устойчиво с одного IP при ротации логинов", "характер поверхности (один IP, ротация логинов)");

        // then: порог алертинга — auth_kdf_operations_total > 100/с на 5 минут.
        AssertFragment(readme, "auth_kdf_operations_total", "имя метрики KDF-операций");
        Assert.True(
            AlertThreshold().IsMatch(readme),
            "README не содержит порог алертинга «auth_kdf_operations_total > 100/с» " +
            "(ожидался фрагмент вида «>100/с» или «> 100/с»).");
        AssertFragment(readme, "5 минут", "интервал оценки порога (5 минут)");
    }

    private static void AssertFragment(string readme, string fragment, string what)
    {
        Assert.True(
            readme.Contains(fragment, StringComparison.Ordinal),
            $"README src/api не содержит {what}: фрагмент «{fragment}» отсутствует " +
            "(FR-004 AC «Реестр амплитуд воспроизводится в README», FR-028 п.4).");
    }

    [GeneratedRegex(">\\s*100\\s*/\\s*с", RegexOptions.Compiled)]
    private static partial Regex AlertThreshold();
}
