using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Помощник MeterListener для Meter «labs.api» (ADR-017/ADR-018). Единственный
/// владелец — тестовая инфраструктура наблюдаемости (T-004, компонент C-012);
/// T-022/T-023 только переиспользуют его (ISS-001). Подписывается на инструменты
/// метра (по умолчанию — на все, опционально — на перечисленные имена), копит
/// измерения и отдаёт суммы (счётчики) и исходные значения (гистограммы) по имени
/// инструмента и меткам. Метки сопоставляются точно: ключ — ordinal, значение —
/// строка в invariant-культуре (метка 200 (int) и "200" (string) неотличимы);
/// порядок меток в запросе не важен. Потокобезопасен (колбэки MeterListener могут
/// приходить из любых потоков). Приращения считаются с момента создания слушателя
/// или последнего Reset().
/// </summary>
public sealed class TestMeterListener : IDisposable
{
    /// <summary>Имя Meter, которое создаёт ObservabilityMiddleware (T-023, ADR-017).</summary>
    public const string DefaultMeterName = "labs.api";

    private readonly MeterListener _listener = new();
    private readonly string _meterName;
    private readonly HashSet<string>? _instrumentNames;
    private readonly object _gate = new();
    private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Единственный публичный конструктор. instrumentNames пуст — подписка на ВСЕ
    /// инструменты метра meterName; иначе — только на перечисленные имена.
    /// </summary>
    public TestMeterListener(string meterName = DefaultMeterName, params string[] instrumentNames)
    {
        _meterName = meterName;
        _instrumentNames = instrumentNames.Length == 0
            ? null
            : new HashSet<string>(instrumentNames, StringComparer.Ordinal);

        // .NET 8: InstrumentPublished — Action<Instrument, MeterListener>; подписка
        // на подходящий инструмент включается вызовом EnableMeasurementEvents.
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != _meterName)
            {
                return;
            }

            if (_instrumentNames is not null && !_instrumentNames.Contains(instrument.Name))
            {
                return;
            }

            listener.EnableMeasurementEvents(instrument);
        };

        EnableCallbacks<byte>();
        EnableCallbacks<short>();
        EnableCallbacks<int>();
        EnableCallbacks<long>();
        EnableCallbacks<float>();
        EnableCallbacks<double>();
        EnableCallbacks<decimal>();

        _listener.Start();
    }

    /// <summary>Сумма измерений счётчика по имени и меткам (0 — ничего не записано).</summary>
    public double CounterIncrement(string instrumentName, params (string Key, object? Value)[] tags) =>
        GetSeries(instrumentName, tags)?.Total ?? 0;

    /// <summary>Исходные значения гистограммы по имени и меткам (пусто — ничего не записано).</summary>
    public IReadOnlyList<double> HistogramValues(string instrumentName, params (string Key, object? Value)[] tags) =>
        GetSeries(instrumentName, tags)?.Values.ToArray() ?? [];

    /// <summary>Количество измерений по имени и меткам.</summary>
    public int MeasurementCount(string instrumentName, params (string Key, object? Value)[] tags) =>
        GetSeries(instrumentName, tags)?.Values.Count ?? 0;

    /// <summary>Обнуляет накопленное: последующие приращения считаются от этого момента.</summary>
    public void Reset()
    {
        lock (_gate) _series.Clear();
    }

    public void Dispose()
    {
        _listener.Dispose();
        lock (_gate) _disposed = true;
    }

    private Series? GetSeries(string instrumentName, (string Key, object? Value)[] tags)
    {
        var key = BuildKey(
            instrumentName,
            tags.Select(tag => new KeyValuePair<string, object?>(tag.Key, tag.Value)).ToArray());
        lock (_gate) return _series.GetValueOrDefault(key);
    }

    private void EnableCallbacks<T>() where T : struct =>
        _listener.SetMeasurementEventCallback<T>((instrument, measurement, tags, _) =>
            Record(instrument.Name, Convert.ToDouble(measurement, CultureInfo.InvariantCulture), tags.ToArray()));

    private void Record(string instrumentName, double value, KeyValuePair<string, object?>[] tags)
    {
        var key = BuildKey(instrumentName, tags);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_series.TryGetValue(key, out var series))
            {
                _series[key] = series = new Series();
            }

            series.Total += value;
            series.Values.Add(value);
        }
    }

    private static string BuildKey(string instrumentName, KeyValuePair<string, object?>[] tags)
    {
        var builder = new StringBuilder(instrumentName);
        foreach (var tag in tags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
        {
            builder.Append('|').Append(tag.Key).Append('=')
                .Append(Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class Series
    {
        public double Total { get; set; }

        public List<double> Values { get; } = [];
    }
}
