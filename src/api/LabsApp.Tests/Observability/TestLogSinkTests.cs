using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Unit-тесты log-sink (AC T-004): захват записей всех категорий (категория,
/// уровень, сообщение, состояние/ключи), очистка между тестами, потокобезопасность
/// записи из параллельных вызовов ILogger.
/// </summary>
public sealed class TestLogSinkTests
{
    private static ILoggerFactory CreateFactory(TestLogSink sink) =>
        LoggerFactory.Create(builder => builder.AddProvider(sink));

    [Fact]
    public void LogInformationWithState_CapturesCategoryLevelMessageAndKeys()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        var logger = loggerFactory.CreateLogger("Api.Audit");

        logger.LogInformation("Вход {Login} в {Moment}", "teacher1", "12:00");

        var record = Assert.Single(sink.Snapshot());
        Assert.Equal("Api.Audit", record.Category);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Вход teacher1 в 12:00", record.Message);
        Assert.Equal("Вход {Login} в {Moment}", record.MessageTemplate);
        Assert.Equal("teacher1", record.State["Login"]);
        Assert.Equal("12:00", record.State["Moment"]);
        Assert.Null(record.Exception);
        Assert.Equal(TimeSpan.Zero, record.TimestampUtc.Offset);
    }

    [Fact]
    public void LogWarningWithException_CapturesException()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        var logger = loggerFactory.CreateLogger("Api.Security");
        var failure = new InvalidOperationException("отказ");

        logger.LogWarning(failure, "Отклонён запрос {Path}", "/api/v1/labs");

        var record = Assert.Single(sink.Snapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Отклонён запрос /api/v1/labs", record.Message);
        Assert.Same(failure, record.Exception);
    }

    [Fact]
    public void State_DoesNotContainOriginalFormatKey()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        var logger = loggerFactory.CreateLogger("Any.Category");

        logger.LogInformation("значение {Value}", 41);

        var record = Assert.Single(sink.Snapshot());
        Assert.False(record.State.ContainsKey("{OriginalFormat}"));
        Assert.Single(record.State);
    }

    [Fact]
    public void ArbitraryCategories_AreCapturedIndependently()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);

        loggerFactory.CreateLogger("Alpha").LogInformation("первая");
        loggerFactory.CreateLogger("Beta.Sub").LogError("вторая");
        loggerFactory.CreateLogger("Alpha").LogWarning("третья");

        var snapshot = sink.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(2, snapshot.Count(record => record.Category == "Alpha"));
        Assert.Single(snapshot, record => record.Category == "Beta.Sub");
        Assert.Single(snapshot, record => record.Level == LogLevel.Error);
    }

    [Fact]
    public void IsEnabled_InformationTrue_NoneFalse()
    {
        using var sink = new TestLogSink();
        var logger = ((ILoggerProvider)sink).CreateLogger("Any.Category");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void Clear_Isolation_NextTestSeesOnlyOwnRecords()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);

        // Тест A: записал события.
        loggerFactory.CreateLogger("Test.A").LogInformation("запись теста A");
        Assert.Equal(1, sink.Count);

        // Очистка и тест B: видит только свои записи.
        sink.Clear();
        loggerFactory.CreateLogger("Test.B").LogWarning("запись теста B");

        var snapshot = sink.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("Test.B", snapshot[0].Category);
        Assert.Equal(LogLevel.Warning, snapshot[0].Level);
    }

    [Fact]
    public void Snapshot_ReturnsCopyNotLiveView()
    {
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        loggerFactory.CreateLogger("Test.A").LogInformation("до очистки");

        var before = sink.Snapshot();
        sink.Clear();
        loggerFactory.CreateLogger("Test.B").LogInformation("после очистки");

        Assert.Single(before);
        Assert.Equal("Test.A", before[0].Category);
        Assert.Equal(1, sink.Count);
        Assert.Equal("Test.B", sink.Snapshot()[0].Category);
    }

    [Fact]
    public async Task ParallelLogging_CapturesEveryRecordFromAllThreads()
    {
        const int writers = 8;
        const int recordsPerWriter = 250;
        using var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        var barrier = new Barrier(writers);

        var writeTasks = Enumerable
            .Range(0, writers)
            .Select(writer => Task.Run(() =>
            {
                var logger = loggerFactory.CreateLogger($"Parallel.Category.{writer}");
                barrier.SignalAndWait();
                for (var index = 0; index < recordsPerWriter; index++)
                {
                    logger.LogInformation("запись {Writer} {Index}", writer, index);
                }
            }))
            .ToArray();

        await Task.WhenAll(writeTasks);

        var snapshot = sink.Snapshot();
        Assert.Equal(writers * recordsPerWriter, snapshot.Count);
        foreach (var writer in Enumerable.Range(0, writers))
        {
            Assert.Equal(
                recordsPerWriter,
                snapshot.Count(record => record.Category == $"Parallel.Category.{writer}"));
        }
    }

    [Fact]
    public void Dispose_StopsAcceptingRecords()
    {
        var sink = new TestLogSink();
        using var loggerFactory = CreateFactory(sink);
        ((IDisposable)sink).Dispose();

        loggerFactory.CreateLogger("After.Dispose").LogInformation("не должен попасть");

        Assert.Equal(0, sink.Count);
    }
}
