using System.Globalization;
using System.Security.Cryptography;
using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LabsApp.Auth;

/// <summary>
/// Реализация IF-002 (FR-005/ADR-007): PBKDF2-HMAC-SHA256
/// (Rfc2898DeriveBytes.Pbkdf2), соль 16 байт CSPRNG, производный ключ 32 байта,
/// итерации НОВЫХ хэшей — Auth__Pbkdf2Iterations (умолчание 210 000; тесты
/// задают меньшее). Формат хранимой строки —
/// «pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;» —
/// параметры читаются из самого хэша, поэтому смена конфигурации не ломает
/// старые хэши (legacy-расхождение тайминга с эталоном принято — AR-004).
/// Проверка — <see cref="CryptographicOperations.FixedTimeEquals"/>; malformed
/// storedHash и кандидат длиннее 128 символов (ASM-015) отклоняются немедленно,
/// без деривации и без инкремента счётчика.
/// Эталонный хэш (VerifyReference): ровно одна деривация при старте
/// (конструирование singleton'а; hosted-прогрев в AddAuthCore) со случайным
/// plaintext'ом и текущими параметрами — метка reference; VerifyReference
/// выполняет ровно одну деривацию против него, результат отбрасывается
/// вызывающим (SEC-001: равномерная стоимость веток отказа).
/// Каждая деривация инкрементирует <see cref="IKdfCounter"/> (включая
/// эталонную). Пароли/хэши не логируются (NFR-006).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Маркер алгоритма в хранимой строке (IF-002).</summary>
    public const string AlgorithmMarker = "pbkdf2-sha256";

    /// <summary>Длина случайной соли новых хэшей, байт (FR-005: 16).</summary>
    public const int SaltSizeBytes = 16;

    /// <summary>Длина производного ключа, байт (SHA-256: 32).</summary>
    public const int HashSizeBytes = 32;

    /// <summary>Верхняя граница длины пароля при проверке (ASM-015) — отказ без KDF.</summary>
    public const int PasswordMaxLength = AuthCoreDefaults.PasswordMaxLength;

    /// <summary>Верхняя граница итераций, принимаемых в Verify (защита от мусорных строк).</summary>
    public const int MaxIterations = 10_000_000;

    /// <summary>Верхняя разумная длина соли в Verify (защита от мусорных строк).</summary>
    public const int MaxSaltSizeBytes = 128;

    private readonly IOptions<AuthOptions> _options;
    private readonly IKdfCounter _kdfCounter;

    // Параметры и эталон фиксированы при старте: plaintext эталона существует
    // только как локальная переменная конструктора и в состоянии не хранится.
    private readonly int _referenceIterations;
    private readonly byte[] _referenceSalt;
    private readonly byte[] _referenceExpected;

    public Pbkdf2PasswordHasher(IOptions<AuthOptions> options, IKdfCounter kdfCounter)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kdfCounter = kdfCounter ?? throw new ArgumentNullException(nameof(kdfCounter));

        // Эталонный хэш создаётся при старте с ТЕКУЩИМИ параметрами (AR-004):
        // ровно одна деривация, метка reference, результат — только хранимая строка.
        var reference = Hash(WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(AuthCoreDefaults.RefreshTokenSizeBytes)), KdfCallers.Reference);
        (_referenceIterations, _referenceSalt, _referenceExpected) = ParseOrThrow(reference);
    }

    /// <inheritdoc/>
    public string Hash(string password, string caller)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(caller);

        var iterations = CurrentIterations();
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        _kdfCounter.Increment(caller);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlgorithmMarker}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}");
    }

    /// <inheritdoc/>
    public bool Verify(string password, string storedHash, string caller)
    {
        if (string.IsNullOrEmpty(password)
            || string.IsNullOrEmpty(storedHash)
            || string.IsNullOrEmpty(caller)
            || password.Length > PasswordMaxLength)
        {
            return false;
        }

        // Malformed storedHash → false БЕЗ деривации и БЕЗ инкремента (IF-002).
        if (!TryParse(storedHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        _kdfCounter.Increment(caller);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <inheritdoc/>
    public bool VerifyReference(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > PasswordMaxLength)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, _referenceSalt, _referenceIterations, HashAlgorithmName.SHA256, HashSizeBytes);

        _kdfCounter.Increment(KdfCallers.Reference);

        // Результат верификации по эталону отбрасывается вызывающим (IF-002).
        return CryptographicOperations.FixedTimeEquals(actual, _referenceExpected);
    }

    private int CurrentIterations()
    {
        var iterations = _options.Value.Pbkdf2Iterations;
        if (iterations is < 1 or > MaxIterations)
        {
            // Дефект конфигурации (Production-guard валидирует на старте — C-001).
            throw new InvalidOperationException(
                "Auth__Pbkdf2Iterations должен быть целым числом от 1 до 10 000 000.");
        }

        return iterations;
    }

    private static (int Iterations, byte[] Salt, byte[] Expected) ParseOrThrow(string stored) =>
        TryParse(stored, out var iterations, out var salt, out var expected)
            ? (iterations, salt, expected)
            : throw new InvalidOperationException(
                "Эталонный хэш не разобран: дефект формата Hash() (IF-002).");

    private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] expected)
    {
        iterations = 0;
        salt = [];
        expected = [];

        var parts = stored.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], AlgorithmMarker, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations is < 1 or > MaxIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return expected.Length == HashSizeBytes
            && salt.Length is >= SaltSizeBytes and <= MaxSaltSizeBytes;
    }
}
