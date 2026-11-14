using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Шов гейта Δkdf (FR-027, IF-002): IKdfCounter — обязательная регистрация
/// Api.Auth.Core; КАЖДАЯ деривация (Verify, VerifyReference, сид, эталон)
/// инкрементирует счётчик ровно на 1, Snapshot() даёт снимок «caller → число
/// дериваций с момента старта хоста». Гейт кейсов: Δkdf на один запрос = 1
/// в КАЖДОЙ ветке 200/401/429 (и 0 на бите JSON) — считается Δ между снимками
/// ДО и ПОСЛЕ запроса, поэтому стартовые деривации (сид преподавателя, эталонные
/// хэши ReferenceHashesInitializer на StartAsync) в Δ не попадают.
///
/// Резолв — через отражение по имени контракта LabsApp.Auth.IKdfCounter: реворк
/// Api.Auth.Core (замена IPasswordHasher на IF-002 с caller-метками) пишется
/// параллельно с этой зоной, и compile-time ссылка на интерфейс сделала бы
/// сборку зоны невозможной до приземления реворка. Отсутствие типа/регистрации —
/// внятный диагноз в сообщении исключения, а не MissingMethodException.
/// </summary>
public sealed class B11Kdf
{
    /// <summary>Полное имя интерфейса счётчика по контракту IF-002.</summary>
    public const string ContractTypeName = "LabsApp.Auth.IKdfCounter";

    private readonly object _counter;
    private readonly MethodInfo _snapshot;

    private B11Kdf(object counter, MethodInfo snapshot)
    {
        _counter = counter;
        _snapshot = snapshot;
    }

    /// <summary>
    /// Резолвит IKdfCounter из DI тестового хоста. Отсутствие — неисполнимость
    /// гейта Δkdf (реализация IF-002 не приземлена/не зарегистрирована).
    /// </summary>
    public static B11Kdf Resolve(IServiceProvider services)
    {
        var contractType = typeof(Program).Assembly.GetType(ContractTypeName)
            ?? throw new InvalidOperationException(
                $"Тип {ContractTypeName} (IF-002) отсутствует в LabsApp: гейт Δkdf (FR-027) "
                + "неисполним — счётчик дериваций не приземлён реворком Api.Auth.Core.");

        var counter = services.GetRequiredService(contractType)
            ?? throw new InvalidOperationException(
                $"{ContractTypeName} (IF-002) не зарегистрирован в DI тестового хоста: "
                + "гейт Δkdf (FR-027) неисполним.");

        var snapshot = contractType.GetMethod("Snapshot", Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"У {ContractTypeName} нет Snapshot(): контракт IF-002 нарушен.");

        return new B11Kdf(counter, snapshot);
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
        if (raw is System.Collections.IEnumerable pairs)
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

    /// <summary>Δkdf запроса: суммарный прирост дериваций между снимками (гейт FR-027).</summary>
    public static long TotalDelta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        Total(after) - Total(before);

    /// <summary>Δkdf по конкретному caller'у (например «login» — кейс TS-037).</summary>
    public static long CallerDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller)
    {
        before.TryGetValue(caller, out var beforeValue);
        after.TryGetValue(caller, out var afterValue);
        return afterValue - beforeValue;
    }
}
