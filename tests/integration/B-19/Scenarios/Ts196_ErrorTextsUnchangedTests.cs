using LabsApp.IntegrationTests.B19.Infrastructure;

namespace LabsApp.IntegrationTests.B19.Scenarios;

/// <summary>
/// TS-196 «Scope: клиентский словарь error-texts.ts не изменён» (scope, P1,
/// FR-026, out_of_scope «Правка клиентского словаря error-texts.ts»).
/// given: файл src/client/app/shared/validation/error-texts.ts (зона проверки);
/// when:  сравнение с эталонной версией ветки (git diff);
/// then:  файл не изменён: серверные дополнения password.max и lab.url.length
///        живут только в ответах API.
/// </summary>
public sealed class Ts196_ErrorTextsUnchangedTests
{
    private const string ErrorTextsRelativePath =
        "src/client/app/shared/validation/error-texts.ts";

    [Fact]
    public void ErrorTextsFile_IsUnchangedAgainstBranchReference()
    {
        // given: файл error-texts.ts (зона проверки).
        var file = Path.Combine(
            RepoPaths.RepositoryRoot, ErrorTextsRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(
            File.Exists(file),
            $"given не выполнен: файл {ErrorTextsRelativePath} отсутствует.");

        // when: сравнение с эталонной версией ветки (git diff HEAD).
        var diff = GitCli.DiffHead(ErrorTextsRelativePath);

        // then: файл не изменён (код 0 и пустой diff).
        Assert.True(
            diff.ExitCode == 0,
            $"git diff HEAD -- {ErrorTextsRelativePath} завершился с кодом {diff.ExitCode}."
            + $"{Environment.NewLine}{diff.OutputTail()}");

        Assert.True(
            string.IsNullOrWhiteSpace(diff.CombinedOutput),
            "then не выполнен: клиентский словарь error-texts.ts изменён относительно эталона "
            + "ветки, а правка клиентского словаря — out_of_scope (серверные дополнения "
            + "password.max и lab.url.length живут только в ответах API)."
            + $"{Environment.NewLine}git diff HEAD -- {ErrorTextsRelativePath}:"
            + $"{Environment.NewLine}{diff.OutputTail()}");
    }
}
