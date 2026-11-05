using System.Collections;
using System.Reflection;
using LabsApp.Auth.RateLimiting;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Инспекция словаря политики движка лимитера (кейсы TS-186/TS-210, NFR-003/FR-003):
/// «размер словаря политики = индивидуальные ключи + '__overflow__'», «у ключа K
/// индивидуальная запись». Публичный шов TrackedKeysCount отдаёт только суммарный
/// размер, поэтому декомпозиция (сколько индивидуальных, что в корзине, есть ли
/// запись ключа) читается отражением приватного словаря ключей
/// (SlidingWindowLimiter._windows: ключ → метки) и служебного хранилища корзины.
/// Формат корзины допускает обе документированные формы: служебный ключ
/// «__overflow__» в словаре (data_design FR-003) либо отдельное приватное поле
/// (_overflowMarks/_overflow). Чтение — между вызовами теста, вне конкуренции
/// с писателями. Изменилась структура — падение с ясным сообщением (обновить
/// инспекцию зоны), не ошибка компиляции.
/// </summary>
public static class B10LimiterInspection
{
    /// <summary>Служебное имя overflow-корзины в словаре политики (FR-003).</summary>
    public const string OverflowKeyName = "__overflow__";

    private const string StoreFieldName = "_store";
    private const string PoliciesFieldName = "_policies";
    private const string PolicyFieldName = "_policy";

    /// <summary>Есть ли у ключа индивидуальная запись в словаре политики (кейс TS-210).</summary>
    public static bool HasIndividualKey(SlidingWindowLimiter engine, string key) =>
        Windows(engine).Contains(key);

    /// <summary>Число индивидуальных ключей в словаре политики (без корзины).</summary>
    public static int IndividualKeysCount(SlidingWindowLimiter engine)
    {
        var windows = Windows(engine);
        return windows.Contains(OverflowKeyName) ? windows.Count - 1 : windows.Count;
    }

    /// <summary>
    /// Число меток в overflow-корзине. Корзина фактического дерева создаётся
    /// ЛЕНИВО (при первом overflow-допуске), поэтому отсутствие корзины ни в
    /// словаре, ни в альтернативном поле означает ПУСТУЮ корзину — 0, а не ошибку
    /// структуры (кейс TS-210 проверяет состояние «до overflow»).
    /// </summary>
    public static int OverflowMarksCount(SlidingWindowLimiter engine)
    {
        var windows = Windows(engine);
        if (windows.Contains(OverflowKeyName) && windows[OverflowKeyName] is IEnumerable keyed)
        {
            return Count(keyed);
        }

        var field = typeof(SlidingWindowLimiter).GetField("_overflowMarks", BindingFlags.Instance | BindingFlags.NonPublic)
                 ?? typeof(SlidingWindowLimiter).GetField("_overflow", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(engine) is IEnumerable marks ? Count(marks) : 0;
    }

    private static IDictionary Windows(SlidingWindowLimiter engine)
    {
        // Движок v2.2 делегирует состояние в IRateLimitStore (FR-024, ADR-026):
        // SlidingWindowLimiter._store (InMemoryRateLimitStore) хранит
        // _policies[политика][ключ] = список меток. Состояние читается отражением
        // по цепочке; отсутствие состояния политики (ни одной попытки в этой
        // политике) — пустой словарь.
        var storeField = typeof(SlidingWindowLimiter).GetField(
            StoreFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(
            storeField is not null,
            $"Инспекция движка лимитера: приватное поле «{StoreFieldName}» не найдено " +
            "(изменилась реализация — обновите инспекцию зоны B-10).");
        var store = storeField!.GetValue(engine);
        var policiesField = store?.GetType().GetField(
            PoliciesFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(
            policiesField is not null,
            $"Инспекция хранилища лимитера: приватное поле «{PoliciesFieldName}» не найдено " +
            "(изменилась реализация — обновите инспекцию зоны B-10).");
        var policyField = typeof(SlidingWindowLimiter).GetField(
            PolicyFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(
            policyField is not null,
            $"Инспекция движка лимитера: приватное поле «{PolicyFieldName}» не найдено " +
            "(изменилась реализация — обновите инспекцию зоны B-10).");
        var policy = (string)policyField!.GetValue(engine)!;
        var policies = (IDictionary)policiesField!.GetValue(store)!;
        return policies.Contains(policy) && policies[policy] is IDictionary keys
            ? keys
            : new Dictionary<string, List<long>>();
    }

    private static int Count(IEnumerable marks)
    {
        var count = 0;
        foreach (var _ in marks)
        {
            count++;
        }

        return count;
    }
}
