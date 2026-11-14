using System.Globalization;
using System.Text.Json;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B22.Infrastructure;

namespace LabsApp.IntegrationTests.B22.Scenarios;

/// <summary>
/// TS-187 (NFR-004, P1): суммарный счётчик KDF логируется раз в 60 с.
///
/// given: тестовый log-sink (B22LogSink); инжектируемые часы (FakeTimeProvider
///        в DI через ConfigureTestServices — ADR-002: 60-секундный лог строится
///        на TimeProvider.CreateTimer); выполнено несколько операций KDF (через
///        реальный компонент IPasswordHasher с метками вызывателя KdfCallers).
/// when:  перевод часов на 60 с (Advance) и выполнение ещё одной операции KDF.
/// then:  в логе появилась запись суммарного значения auth_kdf_operations_total;
///        метки вызывателя учитываются; секреты в записи отсутствуют (NFR-004:
///        логирование не реже раза в 60 с).
///
/// Доработка CR-001 (реворк-раунд тестов): запись суммарного значения якорится
/// по КАТЕГОРИИ ИСТОЧНИКА (ILogger&lt;KdfCounter&gt; → категория — полное имя
/// замороженного типа LabsApp.Auth.KdfCounter, ADR-031) и по значению из
/// КОРИДОРА СНИМКОВ IKdfCounter.Snapshot() (тестовый шов IF-002). Замороженный
/// KdfCounter пишет «Суммарное число операций KDF с метками вызывателя:
/// {Total}.» — без литерала имени метрики и без по-меточной разбивки в записи:
/// имя метрики auth_kdf_operations_total и метки caller живут в проекции Meter
/// (IF-016), а контракт наблюдаемости NFR-004 требует лишь периодического лога
/// СУММАРНОГО значения. «Метки вызывателя учитываются» проверяется через снимки
/// Snapshot() — Δ между снимками по меткам (ADR-007/ADR-031), а не через
/// обязательные по-меточные поля записи лога.
/// Эталон суммарного значения — коридор [снимок до, снимок после]: перевод
/// часов может зажечь тик ДО четвёртой операции (ADR-007: тесты считают Δ).
/// </summary>
public sealed class Ts187_KdfCounterLogTests : IClassFixture<Ts187_KdfCounterLogTests.Fixture>
{
    /// <summary>
    /// Категория журнала периодической записи суммарного значения: полное имя
    /// замороженного типа-источника (ILogger&lt;KdfCounter&gt;).
    /// </summary>
    private static readonly string CounterLogCategory = typeof(KdfCounter).FullName!;

    private readonly Fixture _fixture;

    public Ts187_KdfCounterLogTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост с инжектируемыми часами (FakeTimeProvider в DI).</summary>
    public sealed class Fixture : IDisposable
    {
        public B22WebAppFactory.KdfClock Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public void AfterClockAdvanceBy60s_AndOneMoreKdfOperation_TotalCounter_IsLogged_WithCallerLabels_AndWithoutSecrets()
    {
        var factory = _fixture.Factory;
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();

        // Счётчик доступен тестам (NFR-004, IF-002/ADR-031): сервис IKdfCounter
        // с Snapshot() из DI тестового хоста.
        var counter = factory.Services.GetService<IKdfCounter>();
        Assert.True(
            counter is not null,
            "NFR-004: сервис IKdfCounter не зарегистрирован в DI тестового хоста — "
            + "каждая операция KDF не инкрементирует доступный тестам счётчик "
            + "auth_kdf_operations_total (шов Δkdf-гейтов FR-027).");

        // given: выполнено несколько операций KDF; секреты зафиксированы для
        // проверки «секреты в записи отсутствуют».
        const string secretOne = "B187-Kdf-Secret-Один-1!";
        const string secretTwo = "B187-Kdf-Secret-Два-2!";
        const string secretThree = "B187-Kdf-Secret-Три-3!";
        var secrets = new[] { secretOne, secretTwo, secretThree };

        var storedHash = hasher.Hash(secretOne, KdfCallers.Seed); // операция 1 (метка seed)
        hasher.Hash(secretTwo, KdfCallers.Seed);                  // операция 2 (метка seed)
        hasher.Verify(secretThree, storedHash, KdfCallers.Login); // операция 3 (метка login, несовпадение — 1 деривация)

        var stateBefore = counter.Snapshot();

        // when: перевод часов на 60 с и выполнение ещё одной операции KDF.
        factory.LogSink.Clear();
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        hasher.Verify(secretOne, storedHash, KdfCallers.Login); // операция 4 (метка login, совпадение — 1 деривация)

        var stateAfter = counter.Snapshot();

        // then (1)+(2): в логе появилась запись суммарного значения
        // auth_kdf_operations_total — запись источника LabsApp.Auth.KdfCounter
        // (категория ILogger<KdfCounter>), несущая число из коридора снимков
        // Snapshot() [totalLow..totalHigh]. CR-001: якорение категорией источника
        // и значением из коридора; литерал имени метрики в записи не требуется —
        // замороженный формат записи — «…: {Total}.» (имя метрики — проекция
        // Meter IF-016).
        var totalLow = Math.Min(Total(stateBefore), Total(stateAfter));
        var totalHigh = Math.Max(Total(stateBefore), Total(stateAfter));

        var records = factory.LogSink.Snapshot();
        var totalRecords = records
            .Where(record => string.Equals(record.Category, CounterLogCategory, StringComparison.Ordinal))
            .Where(record => RecordCarriesTotal(record, totalLow, totalHigh))
            .ToList();
        Assert.True(
            totalRecords.Count > 0,
            "NFR-004: после перевода часов на 60 с и ещё одной операции KDF в логе нет записи "
            + "суммарного значения auth_kdf_operations_total (ожидалась запись категории "
            + $"'{CounterLogCategory}' со значением из коридора снимков {totalLow}..{totalHigh}). "
            + "Записи sink после Advance: " + DescribeRecords(records));

        // then (3): метки вызывателя учитываются — счётчик разделяет операции по
        // меткам вызывателя; проверка через тестовый шов IF-002 (Δ между
        // снимками Snapshot(), ADR-007/ADR-031), CR-001.
        var failures = new List<string>();
        CollectCallerLabelFailures(stateBefore, stateAfter, failures);

        // then (4): секреты в записи отсутствуют (NFR-006: лог не раскрывает входы KDF).
        foreach (var record in totalRecords)
        {
            var serialized = SerializeRecord(record);
            foreach (var secret in secrets)
            {
                if (serialized.Contains(secret, StringComparison.Ordinal))
                {
                    failures.Add($"запись содержит секрет — вход KDF-операции: {serialized}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "NFR-004 нарушен: " + string.Join(" | ", failures));
    }

    /// <summary>Суммарное значение снимка IF-002: сумма счётчиков всех меток.</summary>
    private static long Total(IReadOnlyDictionary<string, long> snapshot) =>
        snapshot.Values.Sum();

    /// <summary>Счёт метки вызывателя в снимке (0 — метка отсутствует).</summary>
    private static long LabelCount(IReadOnlyDictionary<string, long> snapshot, string label) =>
        snapshot.TryGetValue(label, out var count) ? count : 0;

    /// <summary>
    /// «Метки вызывателя учитываются» через снимки Snapshot() (Δ между снимками —
    /// ADR-007/ADR-031): дано — 2 операции с меткой seed и 1 с меткой login;
    /// 4-я операция инкрементирует ровно метку login, метка seed не меняется.
    /// </summary>
    private static void CollectCallerLabelFailures(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after,
        List<string> failures)
    {
        var seedBefore = LabelCount(before, KdfCallers.Seed);
        var loginBefore = LabelCount(before, KdfCallers.Login);

        if (seedBefore < 2)
        {
            failures.Add(
                $"метка вызывателя «{KdfCallers.Seed}» учитывает {seedBefore} операций "
                + "после 2 хэширований с этой меткой — разбивка по вызывателям не ведётся "
                + "(каждая деривация инкрементирует счётчик с меткой вызывателя, FR-005/NFR-004)");
        }

        if (loginBefore < 1)
        {
            failures.Add(
                $"метка вызывателя «{KdfCallers.Login}» учитывает {loginBefore} операций "
                + "после проверки пароля с этой меткой — разбивка по вызывателям не ведётся "
                + "(каждая деривация инкрементирует счётчик с меткой вызывателя, FR-005/NFR-004)");
        }

        var seedAfter = LabelCount(after, KdfCallers.Seed);
        var loginAfter = LabelCount(after, KdfCallers.Login);

        if (loginAfter - loginBefore != 1)
        {
            failures.Add(
                $"4-я операция KDF с меткой «{KdfCallers.Login}» изменила счётчик метки на "
                + $"{loginAfter - loginBefore} (ожидается ровно 1): каждая деривация "
                + "инкрементирует счётчик ровно на 1 (FR-005/NFR-004)");
        }

        if (seedAfter != seedBefore)
        {
            failures.Add(
                $"метка вызывателя «{KdfCallers.Seed}» изменилась без операций с этой меткой: "
                + $"{seedBefore} → {seedAfter}");
        }
    }

    /// <summary>
    /// Несёт ли запись точное суммарное значение счётчика из эталонного коридора
    /// снимков: числовое поле структурированного состояния со значением из
    /// коридора ИЛИ точное число как отдельное число (не фрагмент большего) в
    /// сообщении/шаблоне/строковых полях состояния. Записи предварительно
    /// отфильтрованы по категории источника — проверяется «значение в записи
    /// источника», а не произвольное число.
    /// </summary>
    private static bool RecordCarriesTotal(B22LogRecord record, long totalLow, long totalHigh)
    {
        if (record.State.Any(pair =>
                TryGetLong(pair.Value, out var value)
                && value >= totalLow && value <= totalHigh))
        {
            return true;
        }

        var text = string.Join(
            "\n",
            record.Message ?? string.Empty,
            record.MessageTemplate ?? string.Empty,
            JsonSerializer.Serialize(record.State));
        return ContainsStandaloneNumber(text, totalLow, totalHigh);
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

    /// <summary>Числовое ли значение состояния (преобразуемое в long; bool/char — нет).</summary>
    private static bool TryGetLong(object? value, out long result)
    {
        result = 0;
        if (value is null or bool or char)
        {
            return false;
        }

        try
        {
            result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception)
            when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static string SerializeRecord(B22LogRecord record)
    {
        var payload = new
        {
            record.Category,
            Level = record.Level.ToString(),
            record.Message,
            Template = record.MessageTemplate,
            State = record.State,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string DescribeRecords(IReadOnlyList<B22LogRecord> records) =>
        records.Count == 0
            ? "<нет записей>"
            : string.Join(" | ", records.Take(20).Select(SerializeRecord));
}
