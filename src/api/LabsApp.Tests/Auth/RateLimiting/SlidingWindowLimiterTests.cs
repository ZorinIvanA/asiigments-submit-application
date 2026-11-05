using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.Tests.Auth.RateLimiting;

/// <summary>
/// Юнит-проверки движка скользящего окна (IF-006, FR-003, NFR-003):
/// решающее правило (первые L попыток окна, отказ БЕЗ метки), граница окна
/// (метка ровно windowMs назад — вне окна), независимость ключей, потолок
/// MaxTrackedKeys=10000 (константа) и overflow-корзина '__overflow__'
/// (суммарно ≤10001), освобождение слота, потокобезопасность (32 параллельных
/// TryAcquire → ровно 5 true), ShouldBlock без записи, время — FakeTimeProvider.
/// </summary>
public sealed class SlidingWindowLimiterTests
{
    private const long Window = 60_000;
    private const int Limit = 5;

    // ------------------------------------------------------------------
    // FR-003 AC «Граница лимита в окне» + «отказ метку не пишет».
    // ------------------------------------------------------------------

    [Fact]
    public void TryAcquire_FirstLimitAllowed_RefusalsDoNotExtendWindow()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var attempt = 1; attempt <= Limit; attempt++)
        {
            Assert.True(engine.TryAcquire("key", Window, Limit), $"попытка {attempt}");
        }

        Assert.False(engine.TryAcquire("key", Window, Limit));

        // Отказы непосредственно до края окна: если бы метка отказов
        // добавлялась, окно продлевалось бы, и после +61с попытка осталась бы
        // заблокированной.
        time.Advance(TimeSpan.FromSeconds(59));
        for (var rejection = 0; rejection < 10; rejection++)
        {
            Assert.False(engine.TryAcquire("key", Window, Limit));
        }

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(engine.TryAcquire("key", Window, Limit));
    }

    // ------------------------------------------------------------------
    // FR-003 AC «Граница окна»: метка ровно windowMs назад — ВНЕ окна.
    // ------------------------------------------------------------------

    [Fact]
    public void TryAcquire_MarkExactlyWindowMsOld_IsOutsideWindow()
    {
        // AC подзадачи: window=60000, limit=5, 4 метки ключа K → TryAcquire
        // дважды: true (пятая), false (отказ не пишет); затем метка t0 и
        // TryAcquire(K, t0+60000) → true.
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);
        var t0 = time.GetUtcNow();

        // 4 метки в окне (t0 — первая), затем пятая допущена, шестая отклонена.
        Assert.True(engine.TryAcquire("K", Window, Limit)); // метка t0
        time.Advance(TimeSpan.FromMilliseconds(1));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(engine.TryAcquire("K", Window, Limit));
        }

        Assert.True(engine.TryAcquire("K", Window, Limit)); // 5-я метка
        Assert.False(engine.TryAcquire("K", Window, Limit)); // отказ — метки по-прежнему 5

        // Ровно t0+60000: метка t0 вычищена (порог «строго новее»), слот свободен.
        time.SetUtcNow(t0 + TimeSpan.FromMilliseconds(Window));
        Assert.True(engine.TryAcquire("K", Window, Limit));
    }

    [Fact]
    public void TryAcquire_MarkOneMsBeforeBoundary_StillInsideWindow()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        Assert.True(engine.TryAcquire("k", Window, 1));
        time.Advance(TimeSpan.FromMilliseconds(Window - 1));
        Assert.False(engine.TryAcquire("k", Window, 1));

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(engine.TryAcquire("k", Window, 1));
    }

    // ------------------------------------------------------------------
    // FR-003 AC «Ключи независимы».
    // ------------------------------------------------------------------

    [Fact]
    public void Keys_AreIndependent()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.True(engine.TryAcquire("A", Window, Limit));
        }

        Assert.False(engine.TryAcquire("A", Window, Limit));
        Assert.True(engine.TryAcquire("B", Window, Limit));
    }

    // ------------------------------------------------------------------
    // FR-003 AC «Потолок ключей и overflow» (AC подзадачи «Потолок и overflow»).
    // ------------------------------------------------------------------

    [Fact]
    public void Ceiling_NewKeysBeyondMaxTrackedKeys_ShareOverflowBucket()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        // 10000 индивидуальных ключей с живыми метками.
        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", Window, Limit));
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, engine.TrackedKeysCount);

        // Новые K10001 и K10002 обслуживаются одной корзиной '__overflow__':
        // записей по-прежнему ≤10001 (индивидуальный слот не выделяется).
        Assert.True(engine.TryAcquire("K10001", Window, Limit));
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, engine.TrackedKeysCount);
        Assert.True(engine.TryAcquire("K10002", Window, Limit));
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, engine.TrackedKeysCount);

        // Корзина живёт по тем же (окно, лимит): 3-я..5-я метки допущены,
        // 6-й суммарный запрос в корзине за окно отклоняется.
        Assert.True(engine.TryAcquire("K10003", Window, Limit));
        Assert.True(engine.TryAcquire("K10004", Window, Limit));
        Assert.True(engine.TryAcquire("K10005", Window, Limit));
        Assert.False(engine.TryAcquire("K10006", Window, Limit));

        // Словарь ≤10001.
        Assert.True(engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1);
    }

    [Fact]
    public void Ceiling_BucketHonorsWindowSlide()
    {
        // Корзина живёт по тем же (окно, лимит), что и вызвавший её лимитер:
        // после скольжения окна её метки вычищаются и попытки вновь допускаются.
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", 1_000, 1));
        }

        Assert.True(engine.TryAcquire("b1", 1_000, 1));   // корзина, 1-е попадание
        Assert.False(engine.TryAcquire("b2", 1_000, 1));  // корзина исчерпана
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, engine.TrackedKeysCount);

        time.Advance(TimeSpan.FromMilliseconds(1_001));
        Assert.True(engine.TryAcquire("b3", 1_000, 1));   // корзина и старые ключи вычищены
        Assert.Equal(1, engine.TrackedKeysCount);
    }

    // ------------------------------------------------------------------
    // FR-003 AC «Освобождение слота после окна».
    // ------------------------------------------------------------------

    [Fact]
    public void Capacity_SlotFreedAfterWindow_OldKeysRemoved()
    {
        // FR-003 AC «Освобождение слота после окна»: метки 10000 ключей истекли
        // (время за границей окна) → при первом же запросе с новым ключом полная
        // вычистка удаляет опустевшие записи, новому ключу выделяется
        // индивидуальный слот.
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", 60_000, 1));
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, engine.TrackedKeysCount);
        time.Advance(TimeSpan.FromSeconds(61));

        // Полная вычистка перед решением о ёмкости: опустевшие ключи удалены,
        // слот освобождён (корзина не потребовалась).
        Assert.True(engine.TryAcquire("key-new", 60_000, 1));
        Assert.Equal(1, engine.TrackedKeysCount);

        // Протухший ключ снова допущен (счётчик начат заново).
        Assert.True(engine.TryAcquire("key-0", 60_000, 1));
        Assert.Equal(2, engine.TrackedKeysCount);
    }

    // ------------------------------------------------------------------
    // NFR-003: 50000 уникальных ключей → размер словаря политики ≤10001.
    // ------------------------------------------------------------------

    [Fact]
    public void Nfr003_FiftyThousandUniqueKeys_DictionaryStaysWithinCeiling()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        var allowed = 0;
        for (var i = 0; i < 50_000; i++)
        {
            if (engine.TryAcquire($"key-{i}", Window, Limit))
            {
                allowed++;
            }
        }

        // 10000 индивидуальных ключей (по одной метке) + первые 5 попаданий корзины.
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + Limit, allowed);
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, engine.TrackedKeysCount);
    }

    // ------------------------------------------------------------------
    // FR-003 AC «Потокобезопасность»: 32 параллельных TryAcquire → ровно 5 true.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ParallelTryAcquire_SameKey_ExactlyLimitAllowed()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);
        const int contenders = 32;
        var results = new bool[contenders];

        using var start = new System.Threading.Barrier(contenders);
        var tasks = Enumerable.Range(0, contenders)
            .Select(index => Task.Run(() =>
            {
                start.SignalAndWait();
                results[index] = engine.TryAcquire("k", Window, Limit);
            }))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(Limit, results.Count(allowed => allowed));
        Assert.Equal(1, engine.TrackedKeysCount);
    }

    // ------------------------------------------------------------------
    // ShouldBlock: та же проверка БЕЗ записи.
    // ------------------------------------------------------------------

    [Fact]
    public void ShouldBlock_PureCheck_DoesNotAddMark()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var i = 0; i < Limit; i++)
        {
            Assert.False(engine.ShouldBlock("k", Window, Limit));
            Assert.True(engine.TryAcquire("k", Window, Limit));
        }

        // Проверка после исчерпания — true, но метку не добавляет: после
        // скольжения окна ключ снова допущен ровно пятью исходными метками.
        Assert.True(engine.ShouldBlock("k", Window, Limit));
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.False(engine.ShouldBlock("k", Window, Limit));
        Assert.True(engine.TryAcquire("k", Window, Limit));
    }

    [Fact]
    public void ShouldBlock_UnknownKeyWithFullDictionary_RoutedToBucket()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", 60_000, 1));
        }

        Assert.True(engine.TryAcquire("b1", 60_000, 1)); // корзина набрала лимит

        // Новый ключ при полном словаре был бы направлен в корзину: она исчерпана.
        Assert.True(engine.ShouldBlock("unknown", 60_000, 1));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.False(engine.ShouldBlock("unknown", 60_000, 1));
    }

    [Fact]
    public void ShouldBlock_UnknownKeyWithFreeSlot_ReturnsFalse()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        // Ключа нет и индивидуальный слот есть — блокировки нет (корзина не при делах).
        Assert.True(engine.TryAcquire("k1", 60_000, 1));
        Assert.False(engine.ShouldBlock("unknown", 60_000, 1));
    }

    // ------------------------------------------------------------------
    // Скольжение по меткам: старые метки выходят из окна поштучно.
    // ------------------------------------------------------------------

    [Fact]
    public void Window_SlidesPerMark_OldMarksExpireIndividually()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        Assert.True(engine.TryAcquire("k", Window, 2));
        time.Advance(TimeSpan.FromSeconds(40));
        Assert.True(engine.TryAcquire("k", Window, 2));

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.False(engine.TryAcquire("k", Window, 2));

        // Первая метка (T0) вышла из окна, вторая (T0+40с) ещё жива.
        time.Advance(TimeSpan.FromSeconds(16));
        Assert.True(engine.TryAcquire("k", Window, 2));
    }

    [Fact]
    public void CapacityPurge_RecomputesOldestAfterTouch_KeepsAliveMarks()
    {
        // Вычистка при каждой проверке (NFR-003): устаревшая метка нетронутого
        // k2 удаляется без его касания, при этом ЖИВАЯ метка обновлённого k1
        // сохраняется — последующая попытка k1 отклоняется её счётом.
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        Assert.True(engine.TryAcquire("k1", 1_000, 1));
        Assert.True(engine.TryAcquire("k2", 1_000, 1));

        time.Advance(TimeSpan.FromMilliseconds(1_001));
        Assert.True(engine.TryAcquire("k1", 1_000, 1)); // k2 протух; k1 обновлён

        time.Advance(TimeSpan.FromMilliseconds(500));
        // Порог T0+1501: метка k1 (T0+1001) жива, метка k2 (T0) устарела.
        Assert.True(engine.TryAcquire("k3", 1_000, 1));
        Assert.Equal(2, engine.TrackedKeysCount);

        // Живая метка k1 не потеряна: следующая попытка k1 отклонена лимитом.
        Assert.False(engine.TryAcquire("k1", 1_000, 1));
    }

    [Fact]
    public void CapacityPurge_BucketMarksExpirePerMark()
    {
        // Метки корзины вычищаются поштучно по общему правилу окна при
        // действующем потолке: индивидуальные ключи к моменту проверки ЖИВЫ
        // (обновлены на T0+900), поэтому потолок сохранён и новые ключи идут
        // в корзину; метка корзины T0 к порогу T0+1 устарела, T0+900 — жива.
        const long window = 1_000;
        const int limit = 3;
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", window, limit));   // метки T0
        }

        Assert.True(engine.TryAcquire("b1", window, limit));     // корзина: [T0]
        time.Advance(TimeSpan.FromMilliseconds(900));

        // Обновление индивидуальных ключей: [T0, T0+900] — после вычистки
        // устаревшей метки T0 ключи остаются живы и держат индивидуальные слоты.
        for (var i = 0; i < SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(engine.TryAcquire($"key-{i}", window, limit));
        }

        Assert.True(engine.TryAcquire("b2", window, limit));     // корзина: [T0, T0+900]
        time.Advance(TimeSpan.FromMilliseconds(101));            // порог T0+1

        // Вычистка при каждой проверке: метки T0 (индивидуальных ключей и
        // корзины) удалены; живая метка корзины T0+900 осталась одна.
        Assert.True(engine.TryAcquire("b3", window, limit));     // корзина: 1-я живая метка
        Assert.True(engine.TryAcquire("b4", window, limit));     // корзина: 2-я живая метка
        Assert.False(engine.TryAcquire("b5", window, limit));    // корзина исчерпана (лимит 3)
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, engine.TrackedKeysCount);
    }

    [Fact]
    public void TryAcquire_NullKey_TreatedAsEmptyString()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        Assert.True(engine.TryAcquire(null, Window, 2));
        Assert.True(engine.TryAcquire(null, Window, 2));
        Assert.False(engine.TryAcquire(null, Window, 2));
        Assert.Equal(1, engine.TrackedKeysCount);
    }

    [Fact]
    public void RetryAfterSeconds_ReportsTimeUntilOldestAliveMarkExpires()
    {
        var time = new FakeTimeProvider();
        var engine = new SlidingWindowLimiter(time);

        Assert.Null(engine.RetryAfterSeconds("unknown", Window, 1));

        Assert.True(engine.TryAcquire("k", Window, 1));
        Assert.True(engine.TryAcquire("other", Window, 1));

        Assert.Equal(60, engine.RetryAfterSeconds("k", Window, 1));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(30, engine.RetryAfterSeconds("k", Window, 1));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(engine.TryAcquire("k", Window, 1));
        Assert.Equal(60, engine.RetryAfterSeconds("k", Window, 1));
    }

    [Fact]
    public void Constructor_AndRuleArguments_Validate()
    {
        var time = new FakeTimeProvider();
        Assert.Throws<ArgumentNullException>(() => new SlidingWindowLimiter(null!));

        var engine = new SlidingWindowLimiter(time);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.TryAcquire("k", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.TryAcquire("k", Window, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ShouldBlock("k", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.RetryAfterSeconds("k", 0, 1));
    }
}
