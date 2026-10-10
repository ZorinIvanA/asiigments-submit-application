using System.Collections;
using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B09.Infrastructure;

/// <summary>
/// Тестовые швы KDF-контракта IF-002 (кейсы TS-022..TS-025, FR-005, NFR-004).
///
/// Контракт IF-002 описывает IPasswordHasher с меткой вызывателя
/// (Hash(password, caller), Verify(password, storedHash, caller)), эталонную
/// проверку VerifyReference(password), параметр итераций Auth__Pbkdf2Iterations
/// и счётчик IKdfCounter.Snapshot(). Зона обязана компилироваться и до, и после
/// волны реворка Api.Auth.Core (образец — StoredPasswordHashVerifier зоны B-02),
/// поэтому обращения к идентичности сигнатур выполняются отражением:
/// перегрузки разрешаются по ИМЕНИ метода, именам и числу параметров
/// (password/storedHash/caller), а константа 'login' приводится к фактическому
/// типу параметра caller (string, enum KdfCaller, оператор преобразования из
/// string) — CR-001. Обе документированные формы IF-002 (с caller и без)
/// принимаются. Отсутствующий член контракта — падение с ясным сообщением
/// (это и есть фиксируемое расхождение с FR-005), а не ошибка компиляции зоны.
///
/// Про Auth__Pbkdf2Iterations (CR-001, вторая часть): шов записывает новое
/// значение ДВУМЯ путями — в свойство Pbkdf2Iterations одиночки
/// IOptions&lt;AuthOptions&gt;.Value (потребители, читающие IOptions/сам объект
/// опций лениво, при каждом вызове) и в конфигурацию «Auth:Pbkdf2Iterations»
/// (запись в данные провайдера конфигурации: достаточно для IOptionsSnapshot
/// и прямых чтений IConfiguration; change token не поднимается — потребители
/// на IOptionsMonitor швом не обновляются, CR-003). Хэшер, снимающий итерации в поле ОДИН раз в конструкторе,
/// швом не покрывается: по кейсу TS-023 («конфигурация изменена на 2000,
/// хранилище не пересоздано» → «новый хэш создаётся с 2000 итераций») чтение
/// итераций из текущих опций при вызове — часть проверяемого контракта TS-023;
/// конструкторное кэширование даст законное падение TS-022/TS-023/TS-024,
/// которое разбирается на стадии run как расхождение с кейсом, а не глушится.
/// </summary>
public static class B09KdfSeams
{
    /// <summary>Метка вызывателя кейсов батча (TS-022: Hash('Str0ng!pass','login')).</summary>
    public const string TestCaller = "login";

    /// <summary>Ключ конфигурации итераций KDF (спека FR-005: Auth__Pbkdf2Iterations).</summary>
    private const string Pbkdf2IterationsConfigKey = "Auth:Pbkdf2Iterations";

    private static readonly Lazy<Type?> KdfCounterType = new(FindKdfCounterType);

    /// <summary>
    /// given «IPasswordHasher с Auth__Pbkdf2Iterations=N»: установка числа итераций
    /// (смена конфигурации в рамках одного процесса — хранилище не пересоздаётся;
    /// TS-023/TS-024). Значение записывается в свойство Pbkdf2Iterations опций Auth
    /// (см. сводку класса о двух путях и о покрытии ленивого чтения).
    /// </summary>
    public static void SetPbkdf2Iterations(B09WebAppFactory factory, int iterations)
    {
        var property = typeof(AuthOptions).GetProperty("Pbkdf2Iterations", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(
            property is not null && property.CanWrite && property.PropertyType == typeof(int),
            "Тестовый шов Auth__Pbkdf2Iterations (FR-005) недоступен: на AuthOptions нет " +
            "свойства int Pbkdf2Iterations { get; set; } — число итераций KDF не конфигурируется " +
            "(кейсы TS-022/TS-023/TS-024 неисполнимы в текущей реализации).");

        // Путь 1: одиночка IOptions<AuthOptions>.Value — потребители, читающие
        // значение опций (или сам объект AuthOptions) при каждом вызове Hash/Verify.
        var options = factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        property!.SetValue(options, iterations);

        // Путь 2: конфигурация «Auth:Pbkdf2Iterations» — запись через индексатор
        // кладёт значение в данные провайдера конфигурации и НЕ поднимает change
        // token корня (CR-003): этого достаточно для IOptionsSnapshot (снимок
        // пересобирается на следующий запрос) и прямых чтений IConfiguration, но
        // потребители на IOptionsMonitor швом НЕ обновляются. Если покрытие
        // monitor-потребителей понадобится, шов должен поднимать reload явно.
        var configuration = factory.Services.GetService<IConfiguration>();
        if (configuration is not null)
        {
            configuration[Pbkdf2IterationsConfigKey] =
                iterations.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Hash('password', 'login') — вызов IPasswordHasher.Hash (1- либо 2-аргформа IF-002).</summary>
    public static string Hash(IPasswordHasher hasher, string password)
    {
        var method = FindContractMethod("Hash", requiredNamePart: "password");
        var arguments = BindArguments(method, passwordParameterName: "password", password, storedHash: null);
        return (string)method.Invoke(hasher, arguments)!;
    }

    /// <summary>
    /// Verify(password, storedHash[, caller]) — привязка аргументов по именам
    /// параметров (порядок (stored, password) и (password, storedHash) обе допустимы,
    /// caller при наличии приводится к фактическому типу — CR-001).
    /// </summary>
    public static bool Verify(IPasswordHasher hasher, string password, string storedHash)
    {
        var method = FindContractMethod("Verify", requiredNamePart: "stored");
        var arguments = BindArguments(method, passwordParameterName: "password", password, storedHash);
        return (bool)method.Invoke(hasher, arguments)!;
    }

    /// <summary>
    /// VerifyReference(password) — эталонная проверка (FR-005). Отсутствие метода —
    /// падение с сообщением (расхождение с IF-002), а не ошибка компиляции.
    /// </summary>
    public static bool VerifyReference(IPasswordHasher hasher, string password)
    {
        var method = FindContractMethod("VerifyReference", requiredNamePart: "password");
        var arguments = BindArguments(method, passwordParameterName: "password", password, storedHash: null);
        return (bool)method.Invoke(hasher, arguments)!;
    }

    /// <summary>
    /// Снимок счётчика дериваций IKdfCounter.Snapshot() (IF-002/NFR-004): метка →
    /// число операций. Отсутствие интерфейса/регистрации — падение с сообщением
    /// (тестовый шов счётчика не реализован).
    /// </summary>
    public static IReadOnlyDictionary<string, long> KdfSnapshot(B09WebAppFactory factory)
    {
        var counterType = KdfCounterType.Value;
        Assert.True(
            counterType is not null,
            "Интерфейс IKdfCounter (IF-002/NFR-004) не найден в сборках LabsApp — " +
            "тестовый шов счётчика операций KDF не реализован.");

        object counter;
        try
        {
            counter = factory.Services.GetRequiredService(counterType!);
        }
        catch (Exception exception)
        {
            Assert.Fail($"IKdfCounter не зарегистрирован в DI тестового хоста: {exception.Message}");
            return null!; // недостижимо: Assert.Fail бросает исключение
        }

        var snapshot = counterType!.GetMethods().FirstOrDefault(candidate =>
            candidate.Name == "Snapshot" && candidate.GetParameters().Length == 0);
        Assert.True(snapshot is not null, "У IKdfCounter нет метода Snapshot() (IF-002).");
        return ToSnapshot(snapshot!.Invoke(counter, null));
    }

    /// <summary>Сумма счётчика по всем меткам.</summary>
    public static long Total(IReadOnlyDictionary<string, long> snapshot) => snapshot.Values.Sum();

    /// <summary>Δkdf — приращение суммарного счётчика между снимками (TS-024).</summary>
    public static long DeltaTotal(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after) =>
        Total(after) - Total(before);

    /// <summary>Приращение счётчика по метке (TS-025: auth_kdf_operations_total{reference} = 1).</summary>
    public static long Delta(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after, string label) =>
        Value(after, label) - Value(before, label);

    /// <summary>Разбивка приращений по меткам — для сообщений об отказе.</summary>
    public static string DeltaBreakdown(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
    {
        var labels = before.Keys.Concat(after.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal);
        return string.Join(", ", labels.Select(label => $"{label}: {Value(before, label)} → {Value(after, label)}"));
    }

    /// <summary>
    /// Поиск члена контракта по имени метода и наличию параметра с ожидаемой частью
    /// имени (CR-001): типы параметров не участвуют в разрешении — IF-002 допускает
    /// и строковую метку вызывателя, и отдельный тип KdfCaller. Из подходящих
    /// перегрузок берётся с наименьшим числом параметров (форма без caller).
    /// </summary>
    private static MethodInfo FindContractMethod(string methodName, string requiredNamePart)
    {
        var method = typeof(IPasswordHasher).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => !candidate.IsGenericMethod
                && string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && candidate.GetParameters().Any(parameter =>
                    parameter.Name?.Contains(requiredNamePart, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        Assert.True(
            method is not null,
            $"IPasswordHasher.{methodName} (IF-002) не найден: нет публичного метода «{methodName}» " +
            $"с параметром «{requiredNamePart}» (ожидались формы по именам password/storedHash/caller).");
        return method!;
    }

    /// <summary>
    /// Привязка аргументов по именам параметров; параметр caller приводится к
    /// фактическому типу (string / enum / оператор преобразования из string).
    /// </summary>
    private static object?[] BindArguments(
        MethodInfo method,
        string passwordParameterName,
        string password,
        string? storedHash)
    {
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        foreach (var parameter in parameters)
        {
            var name = parameter.Name ?? string.Empty;
            if (name.Contains("caller", StringComparison.OrdinalIgnoreCase))
            {
                arguments[parameter.Position] = BindCaller(parameter.ParameterType, TestCaller);
            }
            else if (storedHash is not null && name.Contains("stored", StringComparison.OrdinalIgnoreCase))
            {
                arguments[parameter.Position] = storedHash;
            }
            else if (name.Contains(passwordParameterName, StringComparison.OrdinalIgnoreCase))
            {
                arguments[parameter.Position] = password;
            }
            else
            {
                Assert.Fail(
                    $"IPasswordHasher.{method.Name}: параметр «{name}» не опознаётся адаптером зоны B-09 " +
                    "(ожидались password/storedHash/caller) — обновите B09KdfSeams.");
            }
        }

        return arguments;
    }

    /// <summary>
    /// Приведение константы 'login' к типу параметра caller (CR-001): string — как
    /// есть; enum — Enum.Parse без учёта регистра ('login' → KdfCaller.Login);
    /// далее операторы implicit/explicit из string; затем IConvertible.
    /// </summary>
    private static object? BindCaller(Type parameterType, string caller)
    {
        if (parameterType == typeof(string))
        {
            return caller;
        }

        if (parameterType.IsEnum)
        {
            return Enum.Parse(parameterType, caller, ignoreCase: true);
        }

        var conversion = parameterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                (candidate.Name is "op_Implicit" or "op_Explicit")
                && candidate.GetParameters().Length == 1
                && candidate.GetParameters()[0].ParameterType == typeof(string));
        if (conversion is not null)
        {
            return conversion.Invoke(null, [caller]);
        }

        if (typeof(IConvertible).IsAssignableFrom(parameterType))
        {
            return Convert.ChangeType(caller, parameterType, CultureInfo.InvariantCulture);
        }

        Assert.Fail(
            $"Параметр caller типа {parameterType.FullName} не приводится к строковой метке " +
            "«" + TestCaller + "» (ожидались string, enum, оператор преобразования из string " +
            "или IConvertible) — обновите B09KdfSeams.");
        return null; // недостижимо: Assert.Fail бросает исключение
    }

    private static long Value(IReadOnlyDictionary<string, long> snapshot, string label)
    {
        if (snapshot.TryGetValue(label, out var value))
        {
            return value;
        }

        // Дрейф-толерантность меток: реализация на enum-метках даёт ключи вида
        // 'Reference' — совпадение без учёта регистра, состав меток кейса не меняет.
        foreach (var pair in snapshot)
        {
            if (string.Equals(pair.Key, label, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 0L;
    }

    private static IReadOnlyDictionary<string, long> ToSnapshot(object? raw)
    {
        Assert.True(raw is not null, "IKdfCounter.Snapshot() вернул null.");
        var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);

        if (raw is IEnumerable<KeyValuePair<string, long>> typed)
        {
            foreach (var pair in typed)
            {
                snapshot[pair.Key] = pair.Value;
            }

            return snapshot;
        }

        Assert.True(
            raw is IEnumerable && raw is not string,
            $"IKdfCounter.Snapshot() вернул неожиданный тип {raw!.GetType().FullName} " +
            "(ожидался словарь метка → счёт).");
        foreach (var item in (IEnumerable)raw!)
        {
            Assert.True(item is not null, "IKdfCounter.Snapshot() содержит null-элемент.");
            var entry = item!.GetType();
            var rawKey = entry.GetProperty("Key")?.GetValue(item);
            var key = rawKey as string ?? rawKey?.ToString();
            var value = entry.GetProperty("Value")?.GetValue(item);
            Assert.True(
                key is not null && value is not null,
                $"IKdfCounter.Snapshot(): элемент без пары Key/Value ({entry.FullName}).");
            snapshot[key!] = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        return snapshot;
    }

    private static Type? FindKdfCounterType() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(GetAssemblyTypes)
            .FirstOrDefault(type => type.IsInterface
                && type.Name == "IKdfCounter"
                && (type.Namespace ?? string.Empty).StartsWith("LabsApp", StringComparison.Ordinal));

    private static Type[] GetAssemblyTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException)
        {
            return Type.EmptyTypes;
        }
    }
}
