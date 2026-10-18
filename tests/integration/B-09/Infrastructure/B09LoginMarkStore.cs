using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Чтение числа меток login-лимитера (IF-006) по ключу кейса 'lower(trim(login))|IP':
/// состояние движка — IRateLimitStore (FR-024, публичный интерфейс приложения),
/// ключ строится нормализацией LimiterKeys и разделителем LoginFailureLimiter.
/// Используется кейсами TS-039..TS-044 («меток не добавлено», «метка записана»,
/// «число меток осталось 5»); чтение — вне конкуренции с запросами теста.
/// Изменение формата ключа в реализации даст «метка не найдена» — внятное
/// расхождение на прогоне, а не ложнозелёный результат.
/// </summary>
public static class B09LoginMarkStore
{
    /// <summary>Число живых меток ключа 'login|IP' в политике login (0 — записи нет).</summary>
    public static int MarksCount(B09WebAppFactory factory, string login, string ip)
    {
        var store = factory.Services.GetRequiredService<IRateLimitStore>();
        var key = LimiterKeys.FromLogin(login) + "|" + LimiterKeys.FromIp(ip);
        return store.TryGetMarks(RateLimitPolicies.Login, key, out var marks) ? marks.Count : 0;
    }
}
