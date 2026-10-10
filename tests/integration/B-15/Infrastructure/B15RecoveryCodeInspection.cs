using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Infrastructure;

/// <summary>Состояние живого кода восстановления владельца (инспекция хранилища).</summary>
public sealed record B15RecoveryCodeState(int Attempts, DateTime? UsedAt, DateTime ExpiresAt);

/// <summary>
/// Инспекция живого кода восстановления пользователя в хранилище тестового хоста
/// (проверки then «код погашен (usedAt≠null)» — TS-207, IF-015). «Живой»
/// вычисляет само хранилище: UsedAt == null и ExpiresAt &gt; now; null-результат
/// <see cref="IRecoveryCodeRepository.GetLiveForUser"/> = живых кодов у владельца
/// нет. В пределах TTL 10 минут (истечение исключено временем сценария) живых
/// кодов нет ⇔ код погашен: UsedAt≠null. Отсутствие шва в DI — явная ошибка
/// теста, а не молчаливый пропуск.
/// </summary>
public static class B15RecoveryCodeInspection
{
    /// <summary>Живой код владельца либо null (код погашен/аннулирован).</summary>
    public static B15RecoveryCodeState? FindLiveRecoveryCode(IServiceProvider services, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(services);

        var repository = services.GetService<IRecoveryCodeRepository>()
            ?? throw new InvalidOperationException(
                "IRecoveryCodeRepository не зарегистрирован в DI тестового хоста — " +
                "инспекция живых кодов восстановления недоступна (IF-015).");

        var live = repository.GetLiveForUser(userId);
        return live is null
            ? null
            : new B15RecoveryCodeState(live.Attempts, live.UsedAt, live.ExpiresAt);
    }
}
