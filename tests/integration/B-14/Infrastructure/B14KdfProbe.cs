using System.Globalization;
using System.Reflection;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Доступ к тестовому шву счётчика операций KDF (IF-002/ADR-007: IKdfCounter
/// с Snapshot(); tech solution — «доступ к IKdfCounter.Snapshot() из
/// factory.Services»). Разрешение интерфейса — через отражение ПО ИМЕНИ
/// (IKdfCounter в сборке LabsApp): файлы зоны не должны ломать компиляцию от
/// переименований при параллельном реворке, а семантика задана спекой
/// (IF-002: Snapshot() → словарь «метка вызывателя → число дериваций»).
///
/// «Счётчик KDF обнулён» из given кейсов (TS-073, TS-079, TS-083, TS-207)
/// реализуется ДЕЛЬТАМИ: снимок до и снимок после измеряемого участка —
/// Δkdf не зависит от накопленных дериваций старта/сида.
/// Если шов отсутствует — тест падает с явным диагнозом (отсутствие заданного
/// спекой шва — дефект реализации, а не пропуск проверки).
/// </summary>
public static class B14KdfProbe
{
    /// <summary>
    /// Снимок счётчика «метка вызывателя → число дериваций с момента старта».
    /// </summary>
    public static IReadOnlyDictionary<string, long> Snapshot(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var counterInterface = FindKdfCounterInterface()
            ?? throw new InvalidOperationException(
                "Тестовый шов счётчика KDF не найден: в LabsApp нет интерфейса IKdfCounter " +
                "(IF-002: IKdfCounter.Increment(caller)/Snapshot()).");

        var counter = services.GetService(counterInterface)
            ?? throw new InvalidOperationException(
                "IKdfCounter не зарегистрирован в DI тестового хоста — Δkdf-проверки кейсов " +
                "невозможны (tech solution: доступ к IKdfCounter.Snapshot() из factory.Services).");

        var snapshotMethod = counterInterface.GetMethod("Snapshot", Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                "У IKdfCounter нет метода Snapshot() без параметров (IF-002).");

        var rawSnapshot = snapshotMethod.Invoke(counter, null)
            ?? throw new InvalidOperationException(
                "IKdfCounter.Snapshot() вернул null (IF-002: IReadOnlyDictionary<метка, long>).");

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (rawSnapshot is System.Collections.IEnumerable pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair is null)
                {
                    continue;
                }

                var pairType = pair.GetType();
                var key = pairType.GetProperty("Key")?.GetValue(pair);
                var value = pairType.GetProperty("Value")?.GetValue(pair);
                result[key?.ToString() ?? string.Empty] =
                    Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    /// <summary>
    /// Δkdf по метке вызывателя (например «reset_password» — TS-079) между
    /// снимками до и после измеряемого участка.
    /// </summary>
    public static long CallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller)
    {
        var afterValue = after.TryGetValue(caller, out var a) ? a : 0;
        var beforeValue = before.TryGetValue(caller, out var b) ? b : 0;
        return afterValue - beforeValue;
    }

    /// <summary>Δkdf суммарно по всем меткам между снимками.</summary>
    public static long TotalDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    private static Type? FindKdfCounterInterface()
    {
        var labsAssembly = typeof(LabsApp.Storage.IUserRepository).Assembly;
        try
        {
            return labsAssembly.GetTypes()
                .FirstOrDefault(type => type.IsInterface && type.Name == "IKdfCounter");
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(type => type is not null)
                .Cast<Type>()
                .FirstOrDefault(type => type.IsInterface && type.Name == "IKdfCounter");
        }
    }
}
