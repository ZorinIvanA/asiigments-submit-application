using System.Globalization;
using System.Reflection;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Доступ к тестовому шву счётчика операций KDF (IF-002/ADR-007: IKdfCounter
/// с Snapshot(); tech solution — «доступ к IKdfCounter.Snapshot() из
/// factory.Services»). Разрешение интерфейса — через отражение ПО ИМЕНИ
/// (IKdfCounter в сборке LabsApp): файлы зоны не должны ломать компиляцию от
/// переименований при параллельном реворке Api.Auth.Core, а семантика задана
/// спекой (IF-002: Snapshot() → словарь «метка вызывателя → число дериваций»).
///
/// «Счётчик KDF обнулён» из given кейсов (TS-084, TS-085) реализуется ДЕЛЬТАМИ:
/// снимок до и снимок после измеряемого участка — Δkdf не зависит от накопленных
/// дериваций старта/сида.
/// Если шов отсутствует — тест падает с явным диагнозом (отсутствие заданного
/// спекой шва — дефект реализации, а не пропуск проверки).
/// </summary>
public static class B16KdfProbe
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
                "TS-084/TS-085 невозможны (tech solution: доступ к IKdfCounter.Snapshot() " +
                "из factory.Services).");

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

    /// <summary>Δkdf суммарно по всем меткам между снимками (кейс: «Δkdf=…»).</summary>
    public static long TotalDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();

    /// <summary>
    /// Δkdf по ОДНОЙ метке вызывателя между снимками (кейс TS-083: ровно одна
    /// деривация Verify текущего пароля под меткой change_password —
    /// валидация/хэширование нового не выполнялись).
    /// </summary>
    public static long CallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        (after.TryGetValue(caller, out var afterValue) ? afterValue : 0) -
        (before.TryGetValue(caller, out var beforeValue) ? beforeValue : 0);

    private static Type? FindKdfCounterInterface()
    {
        var labsAssembly = typeof(IUserRepository).Assembly;
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
