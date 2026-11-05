namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// Состояние движка лимитера (IF-006/IF-015, FR-024, ADR-026): метки скользящего
/// окна «ключ → метки (unix-мс)», сгруппированные по имени политики
/// (<see cref="RateLimitPolicies"/>). Служебная overflow-корзина
/// <see cref="SlidingWindowLimiter.OverflowKeyName"/> — обычная запись словаря
/// политики. Реализация — in-memory, состояние процесса сбрасывается рестартом
/// (принято ADR-005); замена на внешнее хранилище не требует правок движка и
/// лимитеров (FR-024: потребители зависят только от интерфейса).
/// Потокобезопасность: каждая операция атомарна; СОСТАВНЫЕ последовательности
/// (проверка + запись метки, полная вычистка) сериализует владелец политики —
/// движок <see cref="SlidingWindowLimiter"/> под собственным локом. Каждая
/// политика обслуживается ровно одним движком (композиция DI: singleton-лимитеры
/// с собственными именами политик в общем экземпляре хранилища).
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Число записей политики: индивидуальные ключи + корзина (когда в ней есть
    /// метки). Инспекция для тестов — NFR-003 (суммарно ≤ MaxTrackedKeys+1).
    /// </summary>
    int TrackedKeysCount(string policy);

    /// <summary>Существующие метки ключа; false — записи нет (не создаётся).</summary>
    bool TryGetMarks(string policy, string key, out List<long> marks);

    /// <summary>Метки ключа; для отсутствующего ключа создаётся запись с пустым списком (занимается слот).</summary>
    List<long> GetOrAddMarks(string policy, string key);

    /// <summary>Удаляет запись ключа (опустевший ключ освобождает индивидуальный слот); true — запись была.</summary>
    bool Remove(string policy, string key);

    /// <summary>Копия перечня ключей политики (для полной вычистки устаревших меток).</summary>
    IReadOnlyList<string> GetKeys(string policy);
}
