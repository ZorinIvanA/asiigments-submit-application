namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// База прикладных лимитеров (IF-006): каждый прикладной лимитер владеет
/// СОБСТВЕННОЙ политикой состояния <see cref="IRateLimitStore"/> (свой словарь
/// ключей и своя overflow-корзина — «общая» корзина общая для ключей одного
/// лимитера, не для разных лимитеров с разными (окно, лимит)) и фиксирует своё
/// (окно, лимит) по матрице FR-004. Потолок ключей — константа движка
/// (<see cref="SlidingWindowLimiter.MaxTrackedKeys"/>) — FR-003.
/// </summary>
public abstract class ApplicationRateLimiter
{
    protected ApplicationRateLimiter(
        TimeProvider timeProvider,
        long windowMs,
        int limit,
        IRateLimitStore? store,
        string policy)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        Engine = new SlidingWindowLimiter(timeProvider, store ?? new InMemoryRateLimitStore(), policy);
        WindowMs = windowMs;
        Limit = limit;
    }

    /// <summary>Собственный движок лимитера (инспекция TrackedKeysCount для тестов).</summary>
    protected SlidingWindowLimiter Engine { get; }

    /// <summary>Окно лимитера, мс (матрица FR-004).</summary>
    protected long WindowMs { get; }

    /// <summary>Лимит попыток в окне (матрица FR-004).</summary>
    protected int Limit { get; }

    /// <summary>Суммарное число отслеживаемых записей политики (включая корзину) — инспекция для тестов.</summary>
    public int TrackedKeysCount => Engine.TrackedKeysCount;

    /// <summary>Решающее правило окна с фиксированными (окно, лимит) лимитера.</summary>
    protected bool AcquireCore(string key) => Engine.TryAcquire(key, WindowMs, Limit);

    /// <summary>Секунды до освобождения окна (Retry-After) с фиксированными (окно, лимит).</summary>
    protected int? RetryAfterCore(string key) => Engine.RetryAfterSeconds(key, WindowMs, Limit);
}

/// <summary>
/// Лимитер регистраций (IF-006, FR-004): 5/3600с на IP, TryAcquire выполняется
/// ДО разбора и валидации тела — считаются ВСЕ попытки (СПО 429 раньше 400).
/// </summary>
public sealed class RegisterLimiter(TimeProvider timeProvider, IRateLimitStore? store = null)
    : ApplicationRateLimiter(timeProvider, RegisterWindowMs, RegisterLimit, store, RateLimitPolicies.Register)
{
    /// <summary>Окно регистраций, мс (матрица FR-004: 1 час).</summary>
    public const long RegisterWindowMs = 3_600_000;

    /// <summary>Лимит регистраций на IP за окно (матрица FR-004: 5).</summary>
    public const int RegisterLimit = 5;

    /// <summary>Допускает попытку регистрации с IP (учитываются все попытки).</summary>
    public bool TryAcquire(string? ip) => AcquireCore(LimiterKeys.FromIp(ip));

    /// <summary>Retry-After для отклонённой попытки с IP; блокировки нет → null.</summary>
    public int? RetryAfterSeconds(string? ip) => RetryAfterCore(LimiterKeys.FromIp(ip));
}

/// <summary>
/// Лимитер неудачных входов (IF-006, FR-004): 5/60с на ключ
/// «lower(trim(login))|IP». Проверка ShouldBlock — ТОЛЬКО на ветке неудачной
/// проверки пароля и ПОСЛЕ ровно одной KDF (FR-004/FR-007); ShouldBlock —
/// чистая блок-проверка без метки; RegisterFailure — фиксация учтённой
/// неудачной попытки по решающему правилу окна. Успешный вход лимитером не
/// опрашивается и не учитывается (FR-007).
/// </summary>
public sealed class LoginFailureLimiter(TimeProvider timeProvider, IRateLimitStore? store = null)
    : ApplicationRateLimiter(timeProvider, FailureWindowMs, FailureLimit, store, RateLimitPolicies.Login)
{
    /// <summary>Окно неудачных входов, мс (матрица FR-004: 60 с).</summary>
    public const long FailureWindowMs = 60_000;

    /// <summary>Лимит неудачных входов на (login, IP) за окно (матрица FR-004: 5).</summary>
    public const int FailureLimit = 5;

    /// <summary>
    /// Блок-проверка на ветке неудачной проверки пароля (после одной KDF):
    /// true — окно исчерпано, вызывающий отвечает 429; метку НЕ пишет.
    /// </summary>
    public bool ShouldBlock(string? login, string? ip) =>
        Engine.ShouldBlock(BuildKey(login, ip), FailureWindowMs, FailureLimit);

    /// <summary>Фиксирует учтённую неудачную попытку входа (метка по решающему правилу окна).</summary>
    public void RegisterFailure(string? login, string? ip) =>
        _ = Engine.TryAcquire(BuildKey(login, ip), FailureWindowMs, FailureLimit);

    /// <summary>Retry-After для заблокированной пары (login, IP); блокировки нет → null.</summary>
    public int? RetryAfterSeconds(string? login, string? ip) =>
        Engine.RetryAfterSeconds(BuildKey(login, ip), FailureWindowMs, FailureLimit);

    private static string BuildKey(string? login, string? ip) =>
        LimiterKeys.FromLogin(login) + "|" + LimiterKeys.FromIp(ip);
}

/// <summary>
/// Лимитер запросов кода восстановления (IF-006, FR-004): 3/3600с на
/// lower(trim(email)); TryAcquire ДО проверки существования email — 429
/// одинаков для существующего и несуществующего адреса (оракула нет); 0
/// операций KDF на recovery-ветках (ASM-005). Подтверждение кода отдельного
/// словаря НЕ имеет (счётчик attempts на коде); это единственная частотная
/// граница recovery-веток.
/// </summary>
public sealed class RecoveryRequestLimiter(TimeProvider timeProvider, IRateLimitStore? store = null)
    : ApplicationRateLimiter(timeProvider, RequestWindowMs, RequestLimit, store, RateLimitPolicies.RecoveryRequest)
{
    /// <summary>Окно запросов кода, мс (матрица FR-004: 1 час).</summary>
    public const long RequestWindowMs = 3_600_000;

    /// <summary>Лимит запросов кода на email за окно (матрица FR-004: 3).</summary>
    public const int RequestLimit = 3;

    /// <summary>Допускает запрос кода для email (учитываются все попытки).</summary>
    public bool TryAcquire(string? email) => AcquireCore(LimiterKeys.FromEmail(email));

    /// <summary>Retry-After для отклонённого запроса по email; блокировки нет → null.</summary>
    public int? RetryAfterSeconds(string? email) => RetryAfterCore(LimiterKeys.FromEmail(email));
}
