namespace LabsApp.IntegrationTests.B13.Infrastructure;

/// <summary>
/// Арифметика гейта Δkdf (FR-027, IF-002/ADR-031) над снимками
/// IKdfCounter.Snapshot() (снимки — шов зоны B13RecoveryHarness.KdfSnapshot):
/// КАЖДАЯ деривация инкрементирует счётчик ровно на 1, гейты кейсов считают Δ
/// между снимками ДО и ПОСЛЕ измеряемого участка, поэтому стартовые деривации
/// (сид преподавателя, DI-сид учёток) в Δ не попадают.
/// </summary>
public static class B13RecoveryKdf
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
