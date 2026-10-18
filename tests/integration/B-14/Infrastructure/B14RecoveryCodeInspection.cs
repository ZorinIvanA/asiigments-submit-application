using System.Reflection;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>Состояние живого кода восстановления владельца (инспекция хранилища).</summary>
public sealed record B14RecoveryCodeState(int Attempts, DateTime? UsedAt, DateTime ExpiresAt);

/// <summary>
/// Инспекция живого кода восстановления пользователя в хранилище тестового хоста
/// (проверки then кейсов TS-074 «attempts=1», TS-073/TS-207 «код погашен»,
/// TS-075 «пятая неверная аннулирует»). Разрешение — через отражение ПО ИМЕНИ,
/// чтобы файлы зоны не ломали компиляцию при консолидации токенных хранилищ
/// (IF-015: IRecoveryCodeRepository.GetLiveForUser сегодня; после реворка —
/// ISecurityTokenRepository-семейство с FindLiveForUser). «Живой» = usedAt=null
/// и expiresAt&gt;now — вычисляет само хранилище; null-результат = живых кодов нет
/// (код погашен/аннулирован/просрочен). Отсутствие шва — явная ошибка теста,
/// а не молчаливый пропуск.
/// </summary>
public static class B14RecoveryCodeInspection
{
    private static readonly string[] LiveLookupMethodNames = ["GetLiveForUser", "FindLiveForUser"];

    /// <summary>
    /// Живой код пользователя (usedAt=null и expiresAt&gt;now) либо null.
    /// </summary>
    public static B14RecoveryCodeState? FindLiveRecoveryCode(IServiceProvider services, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(services);

        var candidates = FindCandidateInterfaces();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "Точка инспекции живых кодов восстановления не найдена: ни один интерфейс " +
                "LabsApp не предоставляет " +
                "GetLiveForUser/FindLiveForUser(userId) (IF-015).");
        }

        foreach (var candidate in candidates)
        {
            var service = services.GetService(candidate);
            if (service is null)
            {
                continue;
            }

            var method = candidate
                .GetMethods()
                .First(IsLiveLookup);
            var live = method.Invoke(service, new object[] { userId });
            if (live is null)
            {
                return null;
            }

            var liveType = live.GetType();
            var attempts = liveType.GetProperty("Attempts")?.GetValue(live);
            var usedAt = liveType.GetProperty("UsedAt")?.GetValue(live);
            var expiresAt = liveType.GetProperty("ExpiresAt")?.GetValue(live);
            if (attempts is null || expiresAt is null)
            {
                throw new InvalidOperationException(
                    $"Запись живого кода {liveType.Name} не содержит Attempts/ExpiresAt — " +
                    "инспекция хранилища недоступна.");
            }

            return new B14RecoveryCodeState(
                (int)attempts,
                usedAt as DateTime?,
                (DateTime)expiresAt);
        }

        throw new InvalidOperationException(
            "Интерфейс живых кодов восстановления найден в LabsApp, но не зарегистрирован " +
            "в DI тестового хоста — инспекция хранилища недоступна.");
    }

    private static List<Type> FindCandidateInterfaces()
    {
        var labsAssembly = typeof(LabsApp.Storage.IUserRepository).Assembly;
        Type[] interfaceTypes;
        try
        {
            interfaceTypes = labsAssembly.GetTypes()
                .Where(type => type.IsInterface)
                .ToArray();
        }
        catch (ReflectionTypeLoadException exception)
        {
            interfaceTypes = exception.Types
                .Where(type => type is not null)
                .Cast<Type>()
                .Where(type => type.IsInterface)
                .ToArray();
        }

        return interfaceTypes
            .Where(type => type.GetMethods().Any(IsLiveLookup))
            .OrderByDescending(GetCandidatePriority)
            .ToList();

        static int GetCandidatePriority(Type type) =>
            type.Name == "IRecoveryCodeRepository" ? 2
            : type.Name == "ISecurityTokenRepository" ? 1
            : 0;
    }

    private static bool IsLiveLookup(MethodInfo method) =>
        LiveLookupMethodNames.Contains(method.Name)
        && method.GetParameters() is { Length: 1 } parameters
        && parameters[0].ParameterType == typeof(Guid);
}
