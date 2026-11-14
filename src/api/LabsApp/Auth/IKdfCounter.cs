namespace LabsApp.Auth;

/// <summary>
/// Метки вызывателя операций KDF (IF-002): контроллеры auth передают login /
/// register / reset_password / change_password, сид — seed, эталонная ветка
/// хэшера — reference. Каждая выполненная деривация инкрементирует счётчик
/// ровно на 1 с меткой вызывателя (FR-005, NFR-004).
/// </summary>
public static class KdfCallers
{
    public const string Login = "login";
    public const string Register = "register";
    public const string ResetPassword = "reset_password";
    public const string ChangePassword = "change_password";
    public const string Seed = "seed";
    public const string Reference = "reference";

    /// <summary>Все допустимые метки — инспекция для тестов и потребителей.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Login, Register, ResetPassword, ChangePassword, Seed, Reference,
    ];
}

/// <summary>
/// Счётчик операций KDF (IF-002, NFR-004): КАЖДАЯ деривация инкрементирует
/// счётчик ровно на 1; <see cref="Snapshot"/> — тестовый шов (гейты Δkdf,
/// FR-027); суммарное значение логируется раз в 60 с (TimeProvider-таймер).
/// Метрики: auth_kdf_operations_total{caller} (IF-016).
/// </summary>
public interface IKdfCounter
{
    /// <summary>Инкрементирует счётчик метки вызывателя ровно на 1 (плюс метрика).</summary>
    void Increment(string caller);

    /// <summary>Копия накопленных значений по меткам — тестовый шов (счётчик доступен тестам).</summary>
    IReadOnlyDictionary<string, long> Snapshot();
}
