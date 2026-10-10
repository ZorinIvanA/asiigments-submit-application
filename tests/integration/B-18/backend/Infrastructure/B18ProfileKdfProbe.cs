using LabsApp.Auth;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B18Profile.Infrastructure;

/// <summary>
/// Доступ к тестовому шву счётчика операций KDF (IF-002/ADR-007/ADR-031:
/// LabsApp.Auth.IKdfCounter с Snapshot(); tech solution — «доступ к
/// IKdfCounter.Snapshot() из factory.Services»). Интерфейс резолвится из DI
/// типом; отсутствие типа/регистрации — явный падёж с диагнозом (отсутствие
/// заданного спекой шва — дефект реализации, а не пропуск проверки).
///
/// «Счётчик KDF обнулён» из given кейсов (TS-093, TS-094, TS-095) реализуется
/// ДЕЛЬТАМИ: снимок до и снимок после измеряемого участка — Δkdf не зависит
/// от накопленных дериваций старта/сида.
/// </summary>
public static class B18ProfileKdfProbe
{
    /// <summary>
    /// Снимок счётчика «метка вызывателя → число дериваций с момента старта».
    /// </summary>
    public static IReadOnlyDictionary<string, long> Snapshot(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var counter = services.GetService<IKdfCounter>()
            ?? throw new InvalidOperationException(
                "IKdfCounter не зарегистрирован в DI тестового хоста — Δkdf-проверки кейсов " +
                "TS-093/TS-094/TS-095 невозможны (IF-002/ADR-031: обязательная DI-регистрация " +
                "LabsApp.Auth.IKdfCounter; tech solution: доступ к IKdfCounter.Snapshot() " +
                "из factory.Services).");

        var rawSnapshot = counter.Snapshot()
            ?? throw new InvalidOperationException(
                "IKdfCounter.Snapshot() вернул null (IF-002: IReadOnlyDictionary<метка, long>).");

        return new Dictionary<string, long>(rawSnapshot, StringComparer.Ordinal);
    }

    /// <summary>Δkdf суммарно по всем меткам между снимками (кейс: «Δkdf=…»).</summary>
    public static long TotalDelta(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after) =>
        after.Values.Sum() - before.Values.Sum();
}
