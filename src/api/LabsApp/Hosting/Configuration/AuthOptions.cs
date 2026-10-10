namespace LabsApp.Hosting.Configuration;

/// <summary>
/// FR-006: параметры Auth__* (TTL токенов и ключ подписи JWT). В Development без
/// явного Auth__JwtKey композиция-корень подставляет эпизодический случайный ключ
/// (см. Program.cs); фиксированного документированного dev-ключа не существует.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string AccessTtlMinutesVariable = "Auth__AccessTtlMinutes";
    public const string RefreshTtlDaysVariable = "Auth__RefreshTtlDays";
    public const string JwtKeyVariable = "Auth__JwtKey";
    public const string Pbkdf2IterationsVariable = "Auth__Pbkdf2Iterations";

    /// <summary>Минимальная длина Auth__JwtKey в Production (FR-030).</summary>
    public const int JwtKeyMinLength = 32;

    public const int DefaultAccessTtlMinutes = 15;
    public const int DefaultRefreshTtlDays = 7;

    /// <summary>Умолчание Auth__Pbkdf2Iterations (FR-005/ASM-004: 210 000; тесты задают меньшее).</summary>
    public const int DefaultPbkdf2Iterations = 210_000;

    /// <summary>
    /// Верхняя граница Auth__Pbkdf2Iterations (валидатор хостинга, любое окружение):
    /// целое от 1 до 10 000 000 — тот же потолок, что и защита Verify в хэшере.
    /// </summary>
    public const int MaxPbkdf2Iterations = 10_000_000;

    public int AccessTtlMinutes { get; set; } = DefaultAccessTtlMinutes;

    public int RefreshTtlDays { get; set; } = DefaultRefreshTtlDays;

    /// <summary>Число итераций PBKDF2-HMAC-SHA256 для НОВЫХ хэшей (FR-005); Verify читает параметры из хэша.</summary>
    public int Pbkdf2Iterations { get; set; } = DefaultPbkdf2Iterations;

    public string? JwtKey { get; set; }
}
