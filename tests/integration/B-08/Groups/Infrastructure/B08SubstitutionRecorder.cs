using System.Collections.Concurrent;
using System.Reflection;

namespace LabsApp.IntegrationTests.B08.Groups.Infrastructure;

/// <summary>
/// Журнал тестовых реализаций кейса TS-165: какие интерфейсы FR-024 подменены
/// в DI и какие вызовы прошли через подмененные реализации во время сценария.
/// Потокобезопасен (сценарий TS-165 может порождать параллельные запросы).
/// </summary>
public sealed class B08SubstitutionRecorder
{
    private readonly ConcurrentDictionary<string, byte> _substituted = new();
    private readonly ConcurrentDictionary<string, int> _invocations = new(StringComparer.Ordinal);

    /// <summary>Отмечает интерфейс как подмененный тестовой реализацией.</summary>
    public void MarkSubstituted(string interfaceName) =>
        _substituted.TryAdd(interfaceName, 0);

    /// <summary>Фиксирует один вызов метода интерфейса через тестовую реализацию.</summary>
    public void RecordInvocation(string interfaceName, string methodName) =>
        _invocations.AddOrUpdate(
            $"{interfaceName}.{methodName}",
            static _ => 1,
            static (_, count) => count + 1);

    /// <summary>Снимок списка подмененных интерфейсов.</summary>
    public IReadOnlyCollection<string> SubstitutedInterfaces => [.. _substituted.Keys];

    /// <summary>Число зафиксированных вызовов ЛЮБЫХ методов интерфейса.</summary>
    public int CountInvocationsOf(string interfaceName) =>
        _invocations.Keys.Count(key =>
            key.StartsWith(interfaceName, StringComparison.Ordinal)
            && key.Length > interfaceName.Length
            && key[interfaceName.Length] == '.');
}

/// <summary>
/// Тестовая реализация ЛЮБОГО интерфейса персистенции FR-024 (TS-165):
/// DispatchProxy-декоратор — фиксирует вызов и переадресует штатному объекту,
/// поэтому базовый сценарий register → login → GET /labs сохраняет контрактное
/// поведение, а тест видит, что поток управления прошёл через подмененные
/// реализации, а не через штатные регистрации. Не компилируется против
/// конкретных членов интерфейса — переживает реворк сигнатур (IF-015).
/// </summary>
// НЕ sealed: DispatchProxy генерирует производный тип во время выполнения.
public class B08SubstitutionProxy : DispatchProxy
{
    private object? _target;
    private string _interfaceName = string.Empty;
    private B08SubstitutionRecorder? _recorder;

    /// <summary>Строит прокси-реализацию interfaceType над штатным экземпляром.</summary>
    public static object Create(object target, Type interfaceType, B08SubstitutionRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(recorder);

        var proxy = DispatchProxy.Create(interfaceType, typeof(B08SubstitutionProxy));
        if (proxy is not B08SubstitutionProxy substitutionProxy)
        {
            throw new InvalidOperationException(
                $"DispatchProxy не создал реализацию {interfaceType.Name} (FR-024, TS-165).");
        }

        substitutionProxy._target = target;
        substitutionProxy._interfaceName = interfaceType.Name;
        substitutionProxy._recorder = recorder;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        _recorder?.RecordInvocation(_interfaceName, targetMethod.Name);

        try
        {
            return targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException exception)
        {
            // Наружу — исключение штатной реализации (например, StorageConflictException),
            // а не обёртка TargetInvocationException: контракт 409 строится вызывающим.
            throw exception.InnerException ?? exception;
        }
    }
}
