using LabsApp.IntegrationTests.B02.Infrastructure;

namespace LabsApp.IntegrationTests.B02.Scenarios;

/// <summary>
/// TS-156 «Scope: клиентский словарь error-texts.ts не расширен серверными текстами»
/// (FR-023, ISS-015/ISS-016; out_of_scope, P2).
///
/// given: файл src/client/app/shared/validation/error-texts.ts после миграции.
/// when:  чтение файла.
/// then:  отсутствуют серверные дополнения 'Пароль — не более 128 символов' и
///        'Ссылка — не более 1000 символов' (правка клиентского словаря — out_of_scope;
///        тексты живут только в ответах API).
///
/// Тексты проверяются посимвольно (Ordinal) и дословно по кейсу: тире — em dash,
/// пробелы обычные. Заодно фиксируется данное кейса «файл существует»: отсутствие
/// файла — нарушение предусловия, а не зелёный пропуск.
/// </summary>
public sealed class Ts156_ClientErrorTextsScopeTests
{
    /// <summary>Серверные дополнения, которых в клиентском словаре быть не должно.</summary>
    private static readonly string[] ServerOnlyAdditions =
    {
        "Пароль — не более 128 символов",
        "Ссылка — не более 1000 символов",
    };

    [Fact]
    public void ClientErrorTextsFile_DoesNotContainServerOnlyAdditions()
    {
        // given: файл клиентского словаря после миграции.
        var path = Path.Combine(
            RepoPaths.SrcClientDirectory, "app", "shared", "validation", "error-texts.ts");
        Assert.True(
            File.Exists(path),
            $"Файл {path} не найден — данное кейса («файл после миграции существует») нарушено.");

        // when: чтение файла.
        var content = File.ReadAllText(path);

        // then: серверных дополнений нет.
        foreach (var addition in ServerOnlyAdditions)
        {
            Assert.False(
                content.Contains(addition, StringComparison.Ordinal),
                $"Клиентский словарь {path} содержит серверный текст «{addition}» — "
                + "правка клиентского словаря вне области (out_of_scope), текст должен жить "
                + "только в ответах API.");
        }
    }
}
