namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Гейт Δkdf recovery/reset-кейсов батча B-12 (TS-064/TS-073/TS-077; FR-027,
/// IF-002/ADR-031): снимки «caller → число дериваций с момента старта хоста» —
/// B12RecoveryHarness.KdfSnapshot (IKdfCounter из DI тестового хоста); «счётчик
/// KDF сброшен» реализуется дельтами — снимок ПОСЛЕ сида, ДО измеряемого участка.
/// Этот класс добавляет суммарные свёртки снимка (Δkdf=0 кейса TS-064 — по всем
/// меткам сразу: recovery/request не выполняет НИ ОДНОЙ деривации, ASM-005).
/// </summary>
public static class B12RecoveryKdf
{
    /// <summary>Сумма дериваций снимка по всем caller'ам.</summary>
    public static long Total(IReadOnlyDictionary<string, long> snapshot)
    {
        var total = 0L;
        foreach (var value in snapshot.Values)
        {
            total += value;
        }

        return total;
    }

    /// <summary>Δkdf измеряемого участка: суммарный прирост дериваций между снимками.</summary>
    public static long TotalDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        Total(after) - Total(before);
}
