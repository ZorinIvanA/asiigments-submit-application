using System.Diagnostics.Metrics;
using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B17.Infrastructure;

/// <summary>
/// Проба KDF-операций кейса TS-068 (then «Δkdf=0», FR-012/FR-004: «recovery/request:
/// 0 операций KDF»): MeterListener, суммирующий ВСЕ измерения инструмента
/// auth_kdf_operations_total (независимо от меток вызывателя — схема меток
/// расширяется владельцем метрики, дельта полной суммы от этого не зависит).
///
/// ВАЖНО: экземпляр обязан создаваться ДО первого обращения к хосту фикстуры
/// (factory.Services/CreateClient): подписка MeterListener ловит только
/// инструменты, опубликованные ПОСЛЕ Start(), а Meter приложения создаётся при
/// построении хоста. В тесте порядок такой: проба → CreateClient → сид →
/// снимок before → запрос → снимок after.
///
/// Корректность дельты обеспечивается xunit.runner.json зоны
/// (parallelizeTestCollections=false): другие тестовые классы зоны, хэширующие
/// пароли DI-сида, не выполняются одновременно с пробой.
/// </summary>
public sealed class B17KdfProbe : IDisposable
{
    private long _total;
    private readonly MeterListener _listener;

    public B17KdfProbe()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Name, KdfCounter.MetricName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref _total, measurement));
        _listener.Start();
    }

    /// <summary>Суммарное число KDF-операций, зафиксированное пробой.</summary>
    public long Total => Interlocked.Read(ref _total);

    public void Dispose() => _listener.Dispose();
}
