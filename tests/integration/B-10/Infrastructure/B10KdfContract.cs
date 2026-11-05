using System.Reflection;
using LabsApp.Auth;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Адаптер контракта IF-002 (кейсы TS-026/TS-027, FR-005): обращения к
/// IPasswordHasher выполняются отражением с привязкой аргументов ПО ИМЕНАМ
/// параметров (password/storedHash/caller) — расхождение формы сигнатур с
/// контрактом даёт падение с ясным сообщением, а не ошибку компиляции зоны
/// (образец — B09KdfSeams зоны B-09).
///
/// VerifyReference(password) — эталонная проверка FR-005 (арбитраж a-039/CR-003):
/// сигнатура IF-002 — VerifyReference(password); вызывающий НЕ передаёт метку —
/// эталонная деривация учитывается счётчиком с ФИКСИРОВАННОЙ меткой 'reference'
/// внутри хэшера (в эталонную ветку метка login/register не передаётся).
/// Отсутствие метода с одним строковым параметром — падение с ясным сообщением
/// (расхождение с замороженным контрактом IF-002).
/// </summary>
public static class B10KdfContract
{
    /// <summary>Метка вызывателя ветки регистрации (кейс TS-026: Hash(x,'register')).</summary>
    public const string CallerRegister = "register";

    /// <summary>Метка вызывателя ветки входа (кейсы TS-026/TS-027: 'login').</summary>
    public const string CallerLogin = "login";

    /// <summary>Hash(password, caller) — хэширование пароля (одна деривация).</summary>
    public static string Hash(IPasswordHasher hasher, string password, string caller)
    {
        var method = typeof(IPasswordHasher).GetMethod("Hash", [typeof(string), typeof(string)]);
        Assert.True(
            method is not null,
            "IPasswordHasher.Hash (IF-002) не найден с сигнатурой (password, caller).");
        return InvokeString(method!, hasher, password, storedHash: null, caller);
    }

    /// <summary>
    /// Verify(password, storedHash, caller) — проверка пароля (ровно одна
    /// деривация для хранимой строки, разобравшейся как формат IF-002).
    /// </summary>
    public static bool Verify(IPasswordHasher hasher, string password, string storedHash, string caller)
    {
        var method = typeof(IPasswordHasher).GetMethod(
            "Verify", [typeof(string), typeof(string), typeof(string)]);
        Assert.True(
            method is not null,
            "IPasswordHasher.Verify (IF-002) не найден с сигнатурой (password, storedHash, caller).");

        var result = method!.Invoke(hasher, BindArguments(method.GetParameters(), password, storedHash, caller));
        return (bool)result!;
    }

    /// <summary>
    /// VerifyReference(password) — эталонная проверка: ровно одна деривация против
    /// эталонного хэша, результат всегда отбрасывается вызывающим (см. шапку класса).
    /// </summary>
    public static bool VerifyReference(IPasswordHasher hasher, string password)
    {
        var method = typeof(IPasswordHasher).GetMethod("VerifyReference", [typeof(string)]);
        Assert.True(
            method is not null,
            "IPasswordHasher.VerifyReference (IF-002, арбитраж a-039/CR-003: сигнатура " +
            "VerifyReference(password), метка 'reference' ставится хэшером) не найден с " +
            "сигнатурой одного строкового параметра.");
        return (bool)method!.Invoke(hasher, [password])!;
    }

    private static string InvokeString(
        MethodInfo method, IPasswordHasher hasher, string password, string? storedHash, string caller) =>
        (string)method.Invoke(hasher, BindArguments(method.GetParameters(), password, storedHash, caller))!;

    private static object?[] BindArguments(ParameterInfo[] parameters, string password, string? storedHash, string caller)
    {
        var arguments = new object?[parameters.Length];
        foreach (var parameter in parameters)
        {
            var name = parameter.Name ?? string.Empty;
            if (name.Contains("caller", StringComparison.OrdinalIgnoreCase))
            {
                arguments[parameter.Position] = caller;
            }
            else if (name.Contains("stored", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(
                    storedHash is not null,
                    $"IPasswordHasher: параметр «{name}» требует хранимую строку, кейс её не передаёт.");
                arguments[parameter.Position] = storedHash;
            }
            else if (name.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                arguments[parameter.Position] = password;
            }
            else
            {
                Assert.Fail(
                    $"IPasswordHasher: параметр «{name}» не опознаётся адаптером зоны B-10 " +
                    "(ожидались password/storedHash/caller) — обновите B10KdfContract.");
            }
        }

        return arguments;
    }
}
