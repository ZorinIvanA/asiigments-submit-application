using LabsApp.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>
/// Доступ к тестовому шву счётчика операций KDF (IF-002/ADR-031: тип
/// LabsApp.Auth.IKdfCounter с Snapshot() — замороженный контракт, DI-регистрация
/// обязательна; доступ из factory.Services). «Счётчик KDF обнулён» из given
/// кейсов (TS-083, TS-207) реализуется ДЕЛЬТАМИ: снимок до и снимок после
/// измеряемого участка — Δkdf не зависит от накопленных дериваций старта/сида.
/// Отсутствие шва в DI — явная ошибка теста (отсутствие заданного спекой шва —
/// дефект реализации, а не пропуск проверки).
/// </summary>
public static class B15KdfProbe
{
    /// <summary>Снимок счётчика «метка вызывателя → число дериваций с момента старта».</summary>
    public static IReadOnlyDictionary<string, long> Snapshot(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var counter = services.GetService<IKdfCounter>()
            ?? throw new InvalidOperationException(
                "IKdfCounter не зарегистрирован в DI тестового хоста — Δkdf-проверки кейсов " +
                "невозможны (IF-002/ADR-031: обязательная DI-регистрация LabsApp.Auth.IKdfCounter).");
        return counter.Snapshot();
    }

    /// <summary>Δkdf по метке вызывателя (например «reset_password») между снимками.</summary>
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
}
