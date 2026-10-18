using LabsApp.IntegrationTests.B20.Infrastructure;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// Кейс батча B-20: TS-141 «Клиент: ng build проходит» — реализован этим
/// классом (канонический файл клиентской зоны B-19
/// Ts176_ClientBuildTests.cs переименован на префикс Ts141 при переиздании
/// реестра).
/// TS-141 (happy_path, P0, FR-026 AC «Сборка клиента», NFR-002
/// «ng build — exit 0»).
/// given: зависимости клиента установлены (npm ci выполнен; ng cli доступен);
/// when:  ng build в каталоге src/client (проект по умолчанию angular.json —
///        «asiigments-submit-application», sourceRoot src/client; команда
///        выполняется из корня workspace);
/// then:  Exit 0 (FR-026 AC «Сборка клиента»).
/// </summary>
[Collection(SerialNgProcessCollection.Name)]
public sealed class Ts141_ClientBuildTests
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task NgBuild_ExitsZero()
    {
        // given: зависимости установлены (npm ci) — свойство окружения прогона;
        // отсутствие node_modules/ng.js диагностируется явным сообщением NgCli.

        // when: ng build.
        var run = await NgCli.RunNgAsync("build", (int)BuildTimeout.TotalMilliseconds);

        // then: Exit 0.
        Assert.True(
            run.ExitCode == 0,
            $"then не выполнен: ng build завершился с кодом {run.ExitCode} "
            + $"(ожидался 0 — FR-026 AC «Сборка клиента», NFR-002)."
            + $"{Environment.NewLine}{run.OutputTail()}");
    }
}
