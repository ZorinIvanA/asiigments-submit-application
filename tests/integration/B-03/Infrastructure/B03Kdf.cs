using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Шов гейта Δkdf (FR-027, IF-002/NFR-004): IKdfCounter — обязательная регистрация
/// Api.Auth.Core; КАЖДАЯ деривация (Hash/Verify/VerifyReference/сид/эталон)
/// инкрементирует счётчик ровно на 1 с меткой вызывателя (caller ∈ {login, register,
/// reset_password, change_password, seed, reference}), Snapshot() даёт снимок
/// «caller → число дериваций с момента старта хоста». Гейт кейсов регистрации:
/// Δkdf(register)=1 на успехе (TS-031/TS-033) и Δkdf=0 до создания пользователя
/// (TS-032/TS-033 второй POST/TS-034/TS-035 — «KDF только при успешном создании»,
/// FR-004(б)). Δ считается между снимками ДО и ПОСЛЕ запроса, поэтому стартовые
/// деривации хоста (сид преподавателя, эталонные хэши) в Δ не попадают.
///
/// Резолв — через отражение по имени контракта LabsApp.Auth.IKdfCounter: реворк
/// Api.Auth.Core (счётчик с метками вызывателя) пишется параллельно с этой зоной,
/// и compile-time ссылка на интерфейс сделала бы сборку зоны невозможной до
/// приземления реворка. Отсутствие типа/регистрации — внятный диагноз в сообщении
/// исключения, а не MissingMethodException (конвенция зон B-09/B-11/B-14).
/// </summary>
public sealed class B03Kdf
{
    /// <summary>Полное имя интерфейса счётчика по контракту IF-002.</summary>
    public const string ContractTypeName = "LabsApp.Auth.IKdfCounter";

    /// <summary>Метка вызывателя регистрационных дериваций (словарь меток FR-004/NFR-004).</summary>
    public const string RegisterCaller = "register";

    private readonly object _counter;
    private readonly MethodInfo _snapshot;

    private B03Kdf(object counter, MethodInfo snapshot)
    {
        _counter = counter;
        _snapshot = snapshot;
    }

    /// <summary>
    /// Резолвит IKdfCounter из DI тестового хоста. Отсутствие — неисполнимость
    /// гейта Δkdf (счётчик дериваций не приземлён реворком Api.Auth.Core).
    /// </summary>
    public static B03Kdf Resolve(IServiceProvider services)
    {
        var contractType = typeof(Program).Assembly.GetType(ContractTypeName)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(GetAssemblyTypes)
                .FirstOrDefault(type => type.IsInterface
                    && type.Name == "IKdfCounter"
                    && (type.Namespace ?? string.Empty).StartsWith("LabsApp", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Тип {ContractTypeName} (IF-002) отсутствует в LabsApp: гейт Δkdf (FR-027) "
                + "неисполним — счётчик дериваций не приземлён реворком Api.Auth.Core.");

        var counter = services.GetRequiredService(contractType);

        var snapshot = contractType.GetMethod("Snapshot", Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"У {ContractTypeName} нет Snapshot(): контракт IF-002 нарушен.");

        return new B03Kdf(counter, snapshot);
    }

    /// <summary>Снимок счётчика: caller → суммарное число дериваций с момента старта хоста.</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        var raw = _snapshot.Invoke(_counter, null)
            ?? throw new InvalidOperationException("IKdfCounter.Snapshot() вернул null — контракт IF-002 нарушен.");

        if (raw is IReadOnlyDictionary<string, long> typed)
        {
            return typed;
        }

        // Толерантность к реализациям с boxing значений (IDictionary<string, object>).
        if (raw is System.Collections.IEnumerable pairs and not string)
        {
            var converted = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                var pairType = pair.GetType();
                var key = pairType.GetProperty("Key")?.GetValue(pair) as string
                    ?? throw new InvalidOperationException(
                        $"Ключ метки счётчика не строка: {pairType} — контракт IF-002 нарушен.");
                var value = pairType.GetProperty("Value")?.GetValue(pair);
                converted[key] = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            return converted;
        }

        throw new InvalidOperationException(
            $"IKdfCounter.Snapshot() вернул {raw.GetType()}: ожидается словарь «caller → long» (IF-002).");
    }

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

    /// <summary>Δkdf запроса: суммарный прирост дериваций между снимками (TS-032/TS-034/TS-035: 0).</summary>
    public static long TotalDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        Total(after) - Total(before);

    /// <summary>Δkdf по метке вызывателя (TS-031/TS-033: Δkdf(register)=1).</summary>
    public static long CallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller)
    {
        before.TryGetValue(caller, out var beforeValue);
        after.TryGetValue(caller, out var afterValue);
        return afterValue - beforeValue;
    }

    private static Type[] GetAssemblyTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException)
        {
            return Type.EmptyTypes;
        }
    }
}
