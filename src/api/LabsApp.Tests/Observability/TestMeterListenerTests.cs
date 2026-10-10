using System.Diagnostics.Metrics;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Unit-тесты помощника MeterListener (AC T-004 «Метрики»): чтение инкрементов
/// счётчиков и значений гистограмм по имени инструмента и меткам, фильтры подписки,
/// сброс, жизненный цикл.
/// </summary>
/// <remarks>
/// Метка метра — СОБСТВЕННОЕ имя (не TestMeterListener.DefaultMeterName):
/// xUnit запускает коллекции тестов параллельно, и слушатель на общее имя
/// «labs.api» цепляется InstrumentPublished'ом к инструментам чужих
/// WebApplicationFactory-приложений других классов (у каждого — свой Meter
/// «labs.api» и гистограмма health_duration_ms) — точные утверждения
/// Assert.Equal(массив) ловили чужие /health-записи (флейк
/// Histogram_IntMeasurement_Captured в прогоне Ts149 внутри B-19).
/// Привязка слушателя к имени по параметру конструктора — здесь и проверяется.
/// </remarks>
public sealed class TestMeterListenerTests
{
    /// <summary>Приватное имя метра класса — изоляция от параллельных suite'ов.</summary>
    private const string MeterName = "labs.api.test";

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    [Fact]
    public void Counter_IncrementsSummedByInstrumentAndTag()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<long>("auth_login_attempts_total");

        counter.Add(1, Tag("result", "success"));
        counter.Add(2, Tag("result", "success"));
        counter.Add(5, Tag("result", "invalid"));

        Assert.Equal(3, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));
        Assert.Equal(5, listener.CounterIncrement("auth_login_attempts_total", ("result", "invalid")));
        Assert.Equal(2, listener.MeasurementCount("auth_login_attempts_total", ("result", "success")));
        Assert.Equal(1, listener.MeasurementCount("auth_login_attempts_total", ("result", "invalid")));
    }

    [Fact]
    public void Counter_DefaultIntMeasurement_Captured()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<int>("security_rejections_total");

        counter.Add(1, Tag("status", 403));
        counter.Add(1, Tag("status", 429));
        counter.Add(2, Tag("status", 403));

        Assert.Equal(3, listener.CounterIncrement("security_rejections_total", ("status", 403)));
        Assert.Equal(1, listener.CounterIncrement("security_rejections_total", ("status", 429)));
    }

    [Fact]
    public void Counter_TagOrderAndValueTypeDoNotAffectMatch()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<long>("requests_total");

        counter.Add(1, Tag("status", 200), Tag("method", "GET"));

        // Порядок меток и тип значения (число против строки) не влияют на сопоставление.
        Assert.Equal(1, listener.CounterIncrement("requests_total", ("method", "GET"), ("status", 200)));
        Assert.Equal(1, listener.CounterIncrement("requests_total", ("method", "GET"), ("status", "200")));
    }

    [Fact]
    public void Histogram_ValuesReturnedByInstrumentAndTags()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var histogram = meter.CreateHistogram<double>("api_request_duration_ms");

        histogram.Record(12.5, Tag("route", "/api/v1/auth/login"), Tag("method", "POST"), Tag("status", 200));
        histogram.Record(48.25, Tag("route", "/api/v1/auth/login"), Tag("method", "POST"), Tag("status", 401));

        Assert.Equal(
            new[] { 12.5 },
            listener.HistogramValues(
                "api_request_duration_ms",
                ("route", "/api/v1/auth/login"), ("method", "POST"), ("status", 200)));
        Assert.Equal(
            new[] { 48.25 },
            listener.HistogramValues(
                "api_request_duration_ms",
                ("route", "/api/v1/auth/login"), ("method", "POST"), ("status", 401)));
    }

    [Fact]
    public void Histogram_IntMeasurement_Captured()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var histogram = meter.CreateHistogram<int>("health_duration_ms");

        histogram.Record(3);

        Assert.Equal(new[] { 3d }, listener.HistogramValues("health_duration_ms"));
        Assert.Equal(3, listener.CounterIncrement("health_duration_ms"));
    }

    [Fact]
    public void Counter_FloatAndDecimalMeasurements_Captured()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var floatCounter = meter.CreateCounter<float>("float_total");
        var decimalHistogram = meter.CreateHistogram<decimal>("decimal_histogram");

        floatCounter.Add(1.5f);
        decimalHistogram.Record(0.25m);

        Assert.Equal(1.5, listener.CounterIncrement("float_total"));
        Assert.Equal(new[] { 0.25 }, listener.HistogramValues("decimal_histogram"));
    }

    [Fact]
    public void UnknownTagValue_ReturnsZeroAndEmpty()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<long>("auth_login_attempts_total");
        var histogram = meter.CreateHistogram<double>("api_request_duration_ms");

        counter.Add(4, Tag("result", "success"));
        histogram.Record(7d, Tag("route", "/health"));

        Assert.Equal(0, listener.CounterIncrement("auth_login_attempts_total", ("result", "invalid")));
        Assert.Empty(listener.HistogramValues("api_request_duration_ms", ("route", "/api/v1/labs")));
        Assert.Equal(0, listener.MeasurementCount("auth_login_attempts_total", ("result", "invalid")));
    }

    [Fact]
    public void OtherMeterName_NotCaptured()
    {
        using var otherMeter = new Meter("other.meter", "1.0");
        using var listener = new TestMeterListener(MeterName);

        otherMeter.CreateCounter<long>("auth_login_attempts_total").Add(7, Tag("result", "success"));

        Assert.Equal(0, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));
        Assert.Equal(0, listener.MeasurementCount("auth_login_attempts_total", ("result", "success")));
    }

    [Fact]
    public void InstrumentNameFilter_CapturesOnlyListedInstruments()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName, "wanted_total");

        meter.CreateCounter<long>("wanted_total").Add(4);
        meter.CreateCounter<long>("unwanted_total").Add(9);

        Assert.Equal(4, listener.CounterIncrement("wanted_total"));
        Assert.Equal(0, listener.CounterIncrement("unwanted_total"));
    }

    [Fact]
    public void ExistingInstrumentBeforeListener_IsCapturedByStart()
    {
        using var meter = new Meter(MeterName, "1.0");
        var counter = meter.CreateCounter<long>("security_audit_events_total");

        using var listener = new TestMeterListener(MeterName);

        counter.Add(2, Tag("op", "logout"));

        Assert.Equal(2, listener.CounterIncrement("security_audit_events_total", ("op", "logout")));
    }

    [Fact]
    public void Reset_DeltasComputedFromResetPoint()
    {
        using var meter = new Meter(MeterName, "1.0");
        using var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<long>("auth_login_attempts_total");

        counter.Add(3, Tag("result", "success"));
        Assert.Equal(3, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));

        listener.Reset();

        Assert.Equal(0, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));
        counter.Add(2, Tag("result", "success"));
        Assert.Equal(2, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));
    }

    [Fact]
    public void Dispose_StopsCapturing_ValuesStillReadable()
    {
        using var meter = new Meter(MeterName, "1.0");
        var listener = new TestMeterListener(MeterName);
        var counter = meter.CreateCounter<long>("auth_login_attempts_total");
        counter.Add(1, Tag("result", "success"));

        listener.Dispose();
        counter.Add(5, Tag("result", "success"));

        Assert.Equal(1, listener.CounterIncrement("auth_login_attempts_total", ("result", "success")));
    }
}
