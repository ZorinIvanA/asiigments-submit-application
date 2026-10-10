using LabsApp.Auth;
using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Гейты кейсов login/refresh/logout-плитки батча B-10 (FR-027):
/// (1) снимок счётчика дериваций IKdfCounter.Snapshot() — замороженный тестовый
/// шов (ADR-031; типизированный доступ — зона B-10 ссылается на LabsApp напрямую,
/// образец Ts026); (2) чтение числа живых меток login-лимитера по ключу кейса
/// 'lower(trim(login))|IP' из IRateLimitStore (FR-024/IF-006). Ключ строится
/// ЗЕРКАЛОМ контракта (нормализация LimiterKeys + дословный разделитель '|'):
/// смена формата ключа в реализации даст «метка не найдена» — внятное падение
/// на прогоне, а не ложнозелёный результат (ADR-042: зеркала обновляются вместе
/// с прод-кодом). Чтение — между вызовами теста, вне конкуренции с запросами.
/// </summary>
public static class B10AuthGates
{
    /// <summary>Снимок счётчика дериваций (метка вызывателя → число операций, IF-002).</summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(B10HostFactory factory) =>
        factory.Services.GetRequiredService<IKdfCounter>().Snapshot();

    /// <summary>Сумма счётчика по всем меткам.</summary>
    public static long Total(IReadOnlyDictionary<string, long> snapshot) => snapshot.Values.Sum();

    /// <summary>Δkdf — приращение суммарного счётчика между снимками (FR-027).</summary>
    public static long DeltaTotal(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        Total(after) - Total(before);

    /// <summary>Приращение счётчика по метке вызывателя (KdfCallers.*).</summary>
    public static long Delta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        string caller) =>
        after.GetValueOrDefault(caller) - before.GetValueOrDefault(caller);

    /// <summary>Разбивка приращений по меткам — диагностика сообщения об отказе.</summary>
    public static string Breakdown(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
    {
        var callers = before.Keys.Concat(after.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(caller => caller, StringComparer.Ordinal);
        return string.Join(
            ", ", callers.Select(caller => $"{caller}: {before.GetValueOrDefault(caller)} → {after.GetValueOrDefault(caller)}"));
    }

    /// <summary>Число живых меток ключа 'lower(trim(login))|IP' в политике login (0 — записи нет).</summary>
    public static int LoginMarksCount(B10HostFactory factory, string login, string ip)
    {
        var store = factory.Services.GetRequiredService<IRateLimitStore>();
        var key = LimiterKeys.FromLogin(login) + "|" + LimiterKeys.FromIp(ip);
        return store.TryGetMarks(RateLimitPolicies.Login, key, out var marks) ? marks.Count : 0;
    }
}
