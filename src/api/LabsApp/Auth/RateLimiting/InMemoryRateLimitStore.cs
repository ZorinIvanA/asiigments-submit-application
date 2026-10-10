namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// In-memory реализация <see cref="IRateLimitStore"/> (IF-006/IF-015, FR-024):
/// «политика → (ключ → метки окна)». Экземпляр-синглтон обслуживает все
/// прикладные политики (<see cref="RateLimitPolicies"/>); изоляция состояния
/// лимитеров — по имени политики. Каждая операция атомарна (общий лок);
/// составные последовательности движка сериализуются его локом поверх этого.
/// </summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, List<long>>> _policies =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int TrackedKeysCount(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) ? keys.Count : 0;
        }
    }

    /// <inheritdoc/>
    public bool TryGetMarks(string policy, string key, out List<long> marks)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (_policies.TryGetValue(policy, out var keys)
                && keys.TryGetValue(key, out var found))
            {
                marks = found;
                return true;
            }
        }

        marks = [];
        return false;
    }

    /// <inheritdoc/>
    public List<long> GetOrAddMarks(string policy, string key)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (!_policies.TryGetValue(policy, out var keys))
            {
                _policies[policy] = keys = [];
            }

            if (!keys.TryGetValue(key, out var marks))
            {
                keys[key] = marks = [];
            }

            return marks;
        }
    }

    /// <inheritdoc/>
    public bool Remove(string policy, string key)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) && keys.Remove(key);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetKeys(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        lock (_gate)
        {
            return _policies.TryGetValue(policy, out var keys) ? [.. keys.Keys] : [];
        }
    }
}
