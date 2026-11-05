using LabsApp.Auth;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

/// <summary>
/// TS-028 «KDF-сервис: эталонная проверка инкрементирует счётчик»
/// (happy_path, FR-005 + NFR-004, P0).
///
/// given: счётчик IKdfCounter обнулён.
/// when:  VerifyReference('anything').
/// then:  возвращено false;
///        auth_kdf_operations_total{endpoint=reference} = 1 (FR-005 AC
///        «Эталонная проверка считается»).
///
/// «Счётчик обнулён» — свежий хост фикстуры (счётчик singleton с нулевыми
/// метками) плюс базовая линия Snapshot() перед шагом: гейт считается по Δ.
/// Метрика auth_kdf_operations_total{caller} — проекция счётчика IKdfCounter
/// (ADR-031), тестовым швом является Snapshot() (кондиции заморозки контракта).
/// </summary>
public sealed class Ts028_VerifyReferenceIncrementsCounterTests
{
    [Fact]
    public void VerifyReference_ReturnsFalse_AndIncrementsReferenceCounterByOne()
    {
        using var factory = new B08AuthDevFactory();

        // Компоненты ядра из DI тестового хоста — РЕАЛЬНЫЕ реализации C-004
        // (заглушка не нужна: внешних систем контракт не имеет).
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var counter = factory.Services.GetRequiredService<IKdfCounter>();

        // given: счётчик обнулён (базовая линия снимка).
        var before = counter.Snapshot();

        // when: VerifyReference('anything').
        var result = hasher.VerifyReference("anything");

        // then: возвращено false (эталонный plaintext создаётся при старте со
        // случайной строкой — совпадение произвольного кандидата невозможно).
        Assert.False(result);

        // then: auth_kdf_operations_total{caller=reference} = 1 — приращение
        // ровно по метке reference, других меток шаг не трогает.
        var delta = B08AuthHost.KdfDelta(before, counter.Snapshot());
        Assert.Equal(1, delta.GetValueOrDefault(KdfCallers.Reference));
        Assert.Equal(1, delta.Total());
        foreach (var caller in KdfCallers.All.Where(caller => caller != KdfCallers.Reference))
        {
            Assert.Equal(0, delta.GetValueOrDefault(caller));
        }
    }
}
