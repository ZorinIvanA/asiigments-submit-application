using System.Diagnostics.Metrics;
using LabsApp.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Зонд счётчика KDF-операций для гейта Δkdf кейсов батча B-07 (TS-208; FR-005:
/// каждая выполненная деривация инкрементирует метрику auth_kdf_operations_total,
/// имя — константа <see cref="KdfCounter.MetricName"/>): локальный
/// MeterListener поверх метрики конкретного тестового хоста. Подписка — ТОЛЬКО
/// на инструменты метра самого хоста (Meter — DI-singleton, Program.cs): тестовые
/// классы xUnit идут параллельно, и счёт хостов соседних фикстур не должен
/// попадать в замер. База зонда на момент Attach — ноль, поэтому
/// <see cref="OperationsCount"/> после сценария — это Δkdf запроса.
/// </summary>
public sealed class B07KdfProbe : IDisposable
{
    private readonly MeterListener _listener;
    private long _operations;

    private B07KdfProbe(MeterListener listener) => _listener = listener;

    /// <summary>Число KDF-операций хоста с момента Attach (Δkdf сценария).</summary>
    public long OperationsCount => Interlocked.Read(ref _operations);

    /// <summary>Подключает зонд к метрике KDF-операций тестового хоста.</summary>
    public static B07KdfProbe Attach(B07WebAppFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var hostMeter = factory.Services.GetRequiredService<Meter>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == KdfCounter.MetricName
                    && ReferenceEquals(instrument.Meter, hostMeter))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        var probe = new B07KdfProbe(listener);
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref probe._operations, measurement));
        listener.Start();
        return probe;
    }

    /// <inheritdoc/>
    public void Dispose() => _listener.Dispose();
}
