namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// Движок скользящего окна (IF-006, FR-003): решающее правило, единое для
/// индивидуальных ключей и overflow-корзины. Метки — unix-мс TimeProvider
/// (глоссарий: детерминированные тесты через FakeTimeProvider):
/// — TryAcquire: перед проверкой вычищаются метки СТРОГО старше now − windowMs
///   (метка возраста ровно windowMs — вне окна) и удаляются опустевшие ключи —
///   при КАЖДОЙ проверке (NFR-003); запрос допускается, если меток ключа в окне
///   &lt; limit — метка now дописывается; отказ метку НЕ дописывает (окно не
///   продлевается);
/// — ShouldBlock: та же проверка БЕЗ записи метки;
/// — потолок MaxTrackedKeys=10000 индивидуальных ключей (КОНСТАНТА, FR-003):
///   НОВЫЙ ключ при полном потолке обслуживается overflow-корзиной — служебным
///   ключом '__overflow__' политики (те же окно и лимит); суммарное число
///   записей политики никогда не превышает MaxTrackedKeys+1 (NFR-003), обход
///   лимита генерацией новых ключей невозможен; освобождение слота — после
///   вычистки опустевших ключей;
/// — Retry-After (SHOULD): чистое чтение без мутации состояния.
/// Глобальный минимум меток политики отслеживается инкрементально: когда он
/// заведомо свежее порога, вычистка пропускается — флуд новыми ключами без
/// устаревших меток не порождает O(словарь) обхода на каждую попытку.
/// Состояние вынесено в <see cref="IRateLimitStore"/> (FR-024, ADR-026);
/// составные операции сериализует лок экземпляра движка. Потокобезопасность —
/// ровно limit допусков при параллельных TryAcquire одного ключа.
/// </summary>
public sealed class SlidingWindowLimiter
{
    /// <summary>Потолок индивидуальных ключей на политику (FR-003, константа).</summary>
    public const int MaxTrackedKeys = 10_000;

    /// <summary>Служебный ключ overflow-корзины в состоянии политики (FR-003).</summary>
    public const string OverflowKeyName = "__overflow__";

    /// <summary>Имя политики изолированного хранилища по умолчанию (собственный store).</summary>
    public const string DefaultPolicyName = "default";

    private readonly object _gate = new();
    private readonly IRateLimitStore _store;
    private readonly string _policy;
    private readonly TimeProvider _timeProvider;

    /// <summary>Минимум всех меток политики; MaxValue — меток нет.</summary>
    private long _oldestMarkMs = long.MaxValue;

    /// <summary>Движок по умолчанию: собственное хранилище <see cref="InMemoryRateLimitStore"/>.</summary>
    public SlidingWindowLimiter(TimeProvider timeProvider)
        : this(timeProvider, new InMemoryRateLimitStore(), DefaultPolicyName)
    {
    }

    /// <param name="timeProvider">Единый источник бизнес-времени.</param>
    /// <param name="store">Состояние окон (FR-024); политика <paramref name="policy"/> — владение этим движком.</param>
    /// <param name="policy">Имя политики состояния (матрица FR-004).</param>
    public SlidingWindowLimiter(TimeProvider timeProvider, IRateLimitStore store, string policy)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);

        _timeProvider = timeProvider;
        _store = store;
        _policy = policy;
    }

    /// <summary>
    /// Число записей политики: индивидуальные ключи + корзина (когда в ней есть
    /// метки). Инспекция для тестов (NFR-003: ≤ MaxTrackedKeys+1); по имени
    /// политики — <see cref="IRateLimitStore.TrackedKeysCount"/>.
    /// </summary>
    public int TrackedKeysCount
    {
        get
        {
            lock (_gate)
            {
                return _store.TrackedKeysCount(_policy);
            }
        }
    }

    /// <summary>Решающее правило окна. Возвращает true — попытка допущена (метка дописана).</summary>
    public bool TryAcquire(string? key, long windowMs, int limit)
    {
        ValidateRule(windowMs, limit);

        lock (_gate)
        {
            var normalized = key ?? string.Empty;
            var nowMs = NowMs();
            var threshold = nowMs - windowMs;

            // FR-003(1)/NFR-003: вычистка меток строго старше now − windowMs и
            // удаление опустевших ключей — при каждой проверке (глобальный минимум
            // пропускает обход, когда устаревших меток заведомо нет).
            PurgeStale(threshold);

            if (_store.TryGetMarks(_policy, normalized, out var marks))
            {
                if (marks.Count >= limit)
                {
                    // Отказ: метка НЕ добавляется — отказы не продлевают окно.
                    return false;
                }

                AddMark(marks, nowMs);
                return true;
            }

            if (IsIndividualSlotAvailable(normalized))
            {
                AddMark(_store.GetOrAddMarks(_policy, normalized), nowMs);
                return true;
            }

            // Потолок индивидуальных ключей: НОВЫЙ ключ обслуживается общей
            // overflow-корзиной '__overflow__' с теми же (окно, лимит).
            return TryAcquireBucket(nowMs, limit);
        }
    }

    /// <summary>
    /// Проверка без записи метки: исчерпан ли ключ — для ShouldBlock лимитера
    /// входа (блок-проверка НЕ добавляет метку). Вычистка — та же, при каждой
    /// проверке; после неё в списках только живые метки.
    /// </summary>
    public bool ShouldBlock(string? key, long windowMs, int limit)
    {
        ValidateRule(windowMs, limit);

        lock (_gate)
        {
            var normalized = key ?? string.Empty;
            var threshold = NowMs() - windowMs;

            PurgeStale(threshold);

            if (_store.TryGetMarks(_policy, normalized, out var own))
            {
                return own.Count >= limit;
            }

            // Ключа нет: индивидуальный слот есть — блокировки нет; слота нет —
            // новый ключ обслуживался бы корзиной.
            return !HasIndividualSlot(normalized)
                && _store.TryGetMarks(_policy, OverflowKeyName, out var bucket)
                && bucket.Count >= limit;
        }
    }

    /// <summary>
    /// Секунды до освобождения окна для ключа (Retry-After, SHOULD): окно
    /// исчерпано (живых меток ≥ лимита) — до истечения самой старой живой метки;
    /// иначе ожидания нет → null. Состояние не мутируется (чистое чтение).
    /// </summary>
    public int? RetryAfterSeconds(string? key, long windowMs, int limit)
    {
        ValidateRule(windowMs, limit);

        lock (_gate)
        {
            var normalized = key ?? string.Empty;
            var nowMs = NowMs();
            var threshold = nowMs - windowMs;

            List<long>? marks;
            if (_store.TryGetMarks(_policy, normalized, out var own))
            {
                marks = own;
            }
            else if (!HasIndividualSlot(normalized)
                && _store.TryGetMarks(_policy, OverflowKeyName, out var bucket))
            {
                marks = bucket;
            }
            else
            {
                return null;
            }

            long? oldestAlive = null;
            var alive = 0;
            foreach (var mark in marks)
            {
                if (mark > threshold)
                {
                    alive++;
                    if (oldestAlive is null || mark < oldestAlive)
                    {
                        oldestAlive = mark;
                    }
                }
            }

            if (alive < limit || oldestAlive is null)
            {
                return null;
            }

            var waitMs = oldestAlive.Value + windowMs - nowMs;
            return waitMs <= 0 ? null : (int)Math.Ceiling(waitMs / 1000.0);
        }
    }

    /// <summary>Допуск через корзину: те же окно и лимит; отказ метку не пишет.</summary>
    private bool TryAcquireBucket(long nowMs, int limit)
    {
        if (!_store.TryGetMarks(_policy, OverflowKeyName, out var bucket))
        {
            bucket = _store.GetOrAddMarks(_policy, OverflowKeyName);
        }

        if (bucket.Count >= limit)
        {
            // Корзина исчерпана: отказ без метки (окно корзины не продлевается).
            return false;
        }

        AddMark(bucket, nowMs);
        return true;
    }

    /// <summary>
    /// Есть ли свободный ИНДИВИДУАЛЬНЫЙ слот для нового ключа (корзина не в счёт).
    /// Вычистка устаревших меток уже выполнена (<see cref="PurgeStale"/>), поэтому
    /// освободившиеся слоты учтены.
    /// </summary>
    private bool IsIndividualSlotAvailable(string normalized) =>
        !string.Equals(normalized, OverflowKeyName, StringComparison.Ordinal)
        && IndividualCount() < MaxTrackedKeys;

    private bool HasIndividualSlot(string normalized) =>
        !string.Equals(normalized, OverflowKeyName, StringComparison.Ordinal)
        && IndividualCount() < MaxTrackedKeys;

    /// <summary>Число индивидуальных ключей политики (служебная корзина не в счёт).</summary>
    private int IndividualCount()
    {
        var total = _store.TrackedKeysCount(_policy);
        return _store.TryGetMarks(_policy, OverflowKeyName, out _) ? total - 1 : total;
    }

    private long NowMs() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private void AddMark(List<long> marks, long markMs)
    {
        marks.Add(markMs);
        if (markMs < _oldestMarkMs)
        {
            _oldestMarkMs = markMs;
        }
    }

    private static void ValidateRule(long windowMs, int limit = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowMs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
    }

    /// <summary>Удаляет метки НЕ строго новее порога (возраст ≥ windowMs — вне окна).</summary>
    private static int RemoveStale(List<long> marks, long threshold) =>
        marks.RemoveAll(mark => mark <= threshold);

    /// <summary>
    /// Полная вычистка политики: метки строго старше порога удаляются у ВСЕХ
    /// ключей и корзины, опустевшие записи удаляются (индивидуальные слоты
    /// освобождаются), минимум пересчитывается точно. Вызов пропускается, когда
    /// глобальный минимум заведомо свежее порога — вычистка тогда эквивалентна
    /// no-op (ни одна метка словаря не старше порога).
    /// </summary>
    private void PurgeStale(long threshold)
    {
        if (_oldestMarkMs > threshold)
        {
            return;
        }

        long min = long.MaxValue;
        List<string>? emptied = null;
        foreach (var key in _store.GetKeys(_policy))
        {
            if (!_store.TryGetMarks(_policy, key, out var marks))
            {
                continue;
            }

            _ = RemoveStale(marks, threshold);
            if (marks.Count == 0)
            {
                (emptied ??= []).Add(key);
            }
            else
            {
                min = Math.Min(min, Min(marks));
            }
        }

        if (emptied is not null)
        {
            foreach (var key in emptied)
            {
                _ = _store.Remove(_policy, key);
            }
        }

        _oldestMarkMs = min;
    }

    private static long Min(List<long> marks)
    {
        var min = long.MaxValue;
        foreach (var mark in marks)
        {
            if (mark < min)
            {
                min = mark;
            }
        }

        return min;
    }
}
