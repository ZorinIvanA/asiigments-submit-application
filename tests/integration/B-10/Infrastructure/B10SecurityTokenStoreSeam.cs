using LabsApp.Domain.Entities;
using System.Reflection;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Тестовый шов хранилища токенов (кейс TS-049, FR-024/NFR-006): спека v2.2
/// консолидирует хранилища в ISecurityTokenRepository (IF-015, остаток задачи
/// T-101: поиск ЖИВОЙ записи — FindLiveByHash), при этом в текущем дереве
/// зарегистрирован IRefreshTokenRepository с поиском записи FindByHash.
/// Шов разрешает ЛЮБОЙ из двух интерфейсов по имени (приоритет — у
/// консолидированного: после приземления T-101 шов переключается на него
/// автоматически) и выбирает имя метода поиска ПО РАЗРЕШЁННОМУ ИНТЕРФЕЙСУ:
/// сначала пробуется имя контракта выбранного интерфейса, затем альтернативное
/// имя — вызов рефлексией и исполнение кейса не зависят ни от того, выполнена
/// ли консолидация хранилищ реворком, ни от того, какое из имён поиска объявлено;
/// форма записи (сущность RefreshToken: TokenHash/ExpiresAt/RevokedAt) —
/// доменная модель и стабильна.
/// </summary>
public sealed class B10SecurityTokenStoreSeam
{
    private static readonly (string InterfaceName, string LookupMethodName)[] CandidateInterfaces =
    {
        ("LabsApp.Storage.ISecurityTokenRepository", "FindLiveByHash"),
        ("LabsApp.Storage.IRefreshTokenRepository", "FindByHash"),
    };

    /// <summary>Все допустимые имена поиска записи по хэшу (порядок приоритета).</summary>
    private static readonly string[] LookupMethodNames = { "FindLiveByHash", "FindByHash" };

    private readonly object _repository;
    private readonly MethodInfo _findByHash;

    public B10SecurityTokenStoreSeam(IServiceProvider services)
    {
        var applicationAssembly = typeof(RefreshToken).Assembly;

        (string InterfaceName, string LookupMethodName)? candidate = null;
        Type? interfaceType = null;
        foreach (var candidateInterface in CandidateInterfaces)
        {
            interfaceType = applicationAssembly.GetType(candidateInterface.InterfaceName);
            if (interfaceType is not null)
            {
                candidate = candidateInterface;
                break;
            }
        }

        Assert.True(
            interfaceType is not null,
            "Не найден шов хранилища токенов: ни ISecurityTokenRepository (IF-015/T-101), ни " +
            "IRefreshTokenRepository (текущее дерево) не существуют в LabsApp.");

        var repository = services.GetService(interfaceType!);
        Assert.True(
            repository is not null,
            $"Шов {interfaceType!.Name} не зарегистрирован в DI приложения.");

        // Имя метода поиска — по контракту РАЗРЕШЁННОГО интерфейса (IF-015:
        // FindLiveByHash у консолидированного ISecurityTokenRepository, FindByHash
        // у прежнего IRefreshTokenRepository); при отсутствии ожидаемого имени —
        // любое из допустимых имён поиска записи по хэшу.
        var findByHash = interfaceType!.GetMethod(candidate!.Value.LookupMethodName, [typeof(string)])
            ?? LookupMethodNames
                .Select(name => interfaceType.GetMethod(name, [typeof(string)]))
                .FirstOrDefault(method => method is not null);
        Assert.True(
            findByHash is not null,
            $"У шва {interfaceType.Name} нет метода поиска записи по хэшу " +
            $"(ожидалось {candidate.Value.LookupMethodName}(string), допустимы также " +
            $"FindLiveByHash(string)/FindByHash(string)) для инспекции записи.");

        _repository = repository;
        _findByHash = findByHash!;
    }

    /// <summary>Запись хранилища по хэшу токена (точное сравнение) либо null.</summary>
    public object? FindByHash(string tokenHash) =>
        _findByHash.Invoke(_repository, [tokenHash]);

    /// <summary>Чтение строкового свойства записи (TokenHash и т.п.).</summary>
    public static string StringProperty(object record, string propertyName) =>
        (string)(RequireProperty(record, propertyName).GetValue(record)
            ?? throw new InvalidOperationException($"Свойство {propertyName} неожиданно null."));

    /// <summary>Чтение DateTime-свойства записи (ExpiresAt/CreatedAt).</summary>
    public static DateTime DateTimeProperty(object record, string propertyName) =>
        (DateTime)(RequireProperty(record, propertyName).GetValue(record)
            ?? throw new InvalidOperationException($"Свойство {propertyName} неожиданно null."));

    /// <summary>Чтение nullable-свойства записи (RevokedAt) без раскрытия типа.</summary>
    public static object? Property(object record, string propertyName) =>
        RequireProperty(record, propertyName).GetValue(record);

    private static PropertyInfo RequireProperty(object record, string propertyName) =>
        record.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"У записи {record.GetType().Name} нет свойства {propertyName}.");
}
