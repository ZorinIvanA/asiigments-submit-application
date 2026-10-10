using LabsApp.Auth;
using LabsApp.IntegrationTests.B20.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B20.Scenarios;

/// <summary>
/// TS-030 (NFR-004, P2): KDF-счётчик логируется раз в 60 секунд.
///
/// given: тестовый log-sink подключён (B20LogSink в конвейере журналирования
///        тестового хоста); счётчик активен; инжектируемые часы на T0
///        (FakeTimeProvider в DI через ConfigureTestServices — ADR-002:
///        60-секундный лог построен на TimeProvider.CreateTimer и следует за
///        фейковым временем); выполнена хотя бы одна операция KDF (реальный
///        компонент IPasswordHasher — единственная KDF-поверхность).
/// when:  перевод часов на T0+60с, затем на T0+120с (Advance — FakeTimeProvider
///        зажигает таймер детерминированно, без реального ожидания).
/// then:  в логе появляются записи суммарного значения счётчика с периодом не
///        реже 60 с (по одной на каждый интервал) (NFR-004, методика verification:
///        проверка записи лога при закреплённом тестовом времени).
///
/// Эталон суммарного значения — сам счётчик (IKdfCounter.Snapshot(), тестовый шов
/// NFR-004): каждая запись должна нести число из коридора снимков [до, после].
/// До истечения первого 60-секундного интервала записей суммарного значения нет —
/// это и есть «период 60 с»: запись появляется на границе интервала, не раньше.
/// </summary>
public sealed class Ts030_KdfCounterLogPeriodTests : IClassFixture<Ts030_KdfCounterLogPeriodTests.Fixture>
{
    /// <summary>Устойчивый фрагмент записи суммарного значения (KdfCounter, Information).</summary>
    private const string CounterRecordMarker = "операций KDF";

    private readonly Fixture _fixture;

    public Ts030_KdfCounterLogPeriodTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост с инжектируемыми часами (FakeTimeProvider в DI) и log-sink.</summary>
    public sealed class Fixture : IDisposable
    {
        public B20ApiFactory.KdfClock Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public void ClockAdvanced_To60s_ThenTo120s_TotalCounterIsLogged_OncePer60sInterval()
    {
        var factory = _fixture.Factory;

        // given: log-sink подключён (свойство фабрики в конвейере журналирования);
        // часы на T0 (свежий FakeTimeProvider — ни одного Advance до шага when);
        // счётчик активен и выполнена хотя бы одна операция KDF — через реальный
        // компонент IPasswordHasher.
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        const string secretOne = "B030-Kdf-Secret-Один-1!";
        const string secretTwo = "B030-Kdf-Secret-Два-2!";
        var storedHash = hasher.Hash(secretOne, KdfCallers.Seed);  // операция 1 (хэширование)
        hasher.Hash(secretTwo, KdfCallers.Seed);                   // операция 2
        hasher.Verify(secretTwo, storedHash, KdfCallers.Login);    // операция 3 (несовпадение — 1 KDF)

        var totalBefore = CounterTotal(factory);
        Assert.True(
            totalBefore > 0,
            "Предусловие NFR-004: после операций KDF суммарное значение счётчика "
            + $"должно быть положительным, фактически {totalBefore}.");

        // Период 60 с: до истечения первого интервала записей суммарного значения нет.
        var recordsAtT0 = CounterTotalRecords(factory.LogSink.Snapshot());
        Assert.True(
            recordsAtT0.Count == 0,
            "NFR-004: до истечения 60-секундного интервала (T0) в логе уже есть записи "
            + $"суммарного значения KDF-счётчика ({recordsAtT0.Count} шт.) — период 60 с нарушен.");

        // when: T0 → T0+60с; затем T0+60с → T0+120с.
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        var recordsAfterFirstInterval = CounterTotalRecords(factory.LogSink.Snapshot());

        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        var recordsAfterSecondInterval = CounterTotalRecords(factory.LogSink.Snapshot());

        var totalAfter = CounterTotal(factory);

        // then (1): по записи на каждый 60-секундный интервал («не реже раза в 60 с»).
        Assert.True(
            recordsAfterFirstInterval.Count >= 1,
            "NFR-004: после перевода часов на T0+60с в логе нет записи суммарного значения "
            + $"KDF-счётчика. Записи sink: {DescribeRecords(factory.LogSink.Snapshot())}.");
        Assert.True(
            recordsAfterSecondInterval.Count >= recordsAfterFirstInterval.Count + 1,
            "NFR-004: после перевода часов на T0+120с в логе нет НОВОЙ записи суммарного "
            + $"значения KDF-счётчика (за второй интервал записей: "
            + $"{recordsAfterSecondInterval.Count - recordsAfterFirstInterval.Count}). "
            + $"Записи sink: {DescribeRecords(factory.LogSink.Snapshot())}.");

        // then (2): записи несут СУМАРНОЕ значение счётчика — число из коридора
        // снимков Snapshot() [totalBefore, totalAfter]; между тиками KDF-операций
        // нет, поэтому оба тика фиксируют одно значение из коридора.
        var corridorFailures = new List<string>();
        foreach (var record in recordsAfterSecondInterval)
        {
            if (!RecordCarriesTotalInRange(record, totalBefore, totalAfter))
            {
                corridorFailures.Add(
                    $"запись не содержит суммарное значение счётчика (коридор снимков "
                    + $"Snapshot(): {totalBefore}..{totalAfter}): {record.Message}");
            }
        }

        // then (3): секреты (входы KDF-операций) в записях отсутствуют (NFR-006).
        foreach (var record in recordsAfterSecondInterval)
        {
            foreach (var secret in new[] { secretOne, secretTwo })
            {
                if (record.Message.Contains(secret, StringComparison.Ordinal))
                {
                    corridorFailures.Add($"запись содержит секрет — вход KDF-операции: {record.Message}");
                }
            }
        }

        Assert.True(
            corridorFailures.Count == 0,
            "NFR-004 нарушен в записях суммарного значения KDF-счётчика: "
            + string.Join(" | ", corridorFailures));
    }

    /// <summary>Записи суммарного значения KDF-счётчика (устойчивый маркер сообщения).</summary>
    private static List<B20LogRecord> CounterTotalRecords(IReadOnlyList<B20LogRecord> records) =>
        records.Where(record => record.Message.Contains(CounterRecordMarker, StringComparison.Ordinal))
            .ToList();

    /// <summary>Суммарное значение счётчика — тестовый шов IKdfCounter.Snapshot() (NFR-004).</summary>
    private static long CounterTotal(B20ApiFactory factory)
    {
        var counter = factory.Services.GetRequiredService<IKdfCounter>();
        return counter.Snapshot().Values.Sum();
    }

    /// <summary>
    /// Несёт ли запись суммарное значение из коридора [low, high]: числовое поле
    /// структурированного состояния ИЛИ отдельное число (не фрагмент большего)
    /// в тексте сообщения.
    /// </summary>
    private static bool RecordCarriesTotalInRange(B20LogRecord record, long low, long high)
    {
        if (record.State.Values
            .Select(TryGetLong)
            .Any(value => value is not null && value >= low && value <= high))
        {
            return true;
        }

        return ContainsStandaloneNumber(record.Message, low, high);
    }

    /// <summary>Числовое ли значение состояния (преобразуемое в long; bool/char — нет).</summary>
    private static long? TryGetLong(object? value)
    {
        if (value is null or bool or char)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value);
        }
        catch (Exception exception)
            when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Есть ли в тексте отдельное число (не часть большего) из диапазона.</summary>
    private static bool ContainsStandaloneNumber(string text, long low, long high)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isDigit = i < text.Length && char.IsDigit(text[i]);
            if (isDigit && start < 0)
            {
                start = i;
            }
            else if (!isDigit && start >= 0)
            {
                if (long.TryParse(text.AsSpan(start, i - start), out var number)
                    && number >= low && number <= high)
                {
                    return true;
                }

                start = -1;
            }
        }

        return false;
    }

    private static string DescribeRecords(IReadOnlyList<B20LogRecord> records) =>
        records.Count == 0
            ? "<нет записей>"
            : string.Join(" | ", records.Take(20)
                .Select(record => $"[{record.Category}] {record.Message}"));
}
