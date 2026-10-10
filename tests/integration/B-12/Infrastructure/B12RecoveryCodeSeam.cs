using System.Reflection;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Шов инспекции записи кода восстановления НЕЗАВИСИМО от живости (then TS-162
/// «код C погашен, usedAt≠null»): консолидированный контракт
/// ISecurityTokenRepository (IF-015) живые записи отдаёт FindLiveForUser, а
/// погашенную — уже нет, поэтому запись читается из внутреннего словаря
/// DI-singleton InMemorySecurityTokenRepository рефлексией ПО ИМЕНИ поля
/// (приём-образец зоны — B12AuthSessions.FindStoredTokenIncludingRevoked).
/// Изменение внутренней формы реализации — громкое падение с диагностикой,
/// а не молчаливый исход.
/// </summary>
public static class B12RecoveryCodeSeam
{
    /// <summary>
    /// Запись кода восстановления по Id независимо от usedAt (копия-снимок) либо
    /// null, если записи с таким Id нет.
    /// </summary>
    public static RecoveryCode? FindByIdIncludingUsed(
        WebApplicationFactory<Program> factory,
        Guid codeId)
    {
        var repository = factory.Services.GetRequiredService<ISecurityTokenRepository>();
        const string InternalCodesFieldName = "_recoveryCodes";
        var codesField = repository.GetType().GetField(
                InternalCodesFieldName,
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Шов инспекции гашения кода неисполним: у {repository.GetType().Name} нет поля "
                + $"<{InternalCodesFieldName}> — внутренняя форма хранилища изменилась, "
                + "инспекция usedAt требует обновления шва зоны B-12.");
        var codesMap = (System.Collections.IDictionary)(codesField.GetValue(repository)
            ?? throw new InvalidOperationException(
                $"Шов инспекции гашения кода неисполним: поле <{InternalCodesFieldName}> равно null."));
        return codesMap.Contains(codeId) ? (RecoveryCode)codesMap[codeId]! : null;
    }
}
