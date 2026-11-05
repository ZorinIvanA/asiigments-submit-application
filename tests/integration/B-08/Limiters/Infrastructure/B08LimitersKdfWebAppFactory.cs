using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;

namespace LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

/// <summary>
/// Фикстура KDF-сценария волны B-08 (TS-025; FR-005): механика
/// <see cref="B08LimitersWebAppFactory"/> (Development) с тестовым значением
/// итераций Auth__Pbkdf2Iterations=1000 — given кейса «IPasswordHasher с
/// Auth__Pbkdf2Iterations=1000 (тестовое значение)»; формат хэша
/// «pbkdf2-sha256$1000$…» проверяется дословно по префиксу.
/// </summary>
public sealed class B08LimitersKdfWebAppFactory : B08LimitersWebAppFactory
{
    /// <summary>Тестовое число итераций PBKDF2 (FR-005: тесты задают меньшее).</summary>
    public const int TestPbkdf2Iterations = 1000;

    public B08LimitersKdfWebAppFactory()
        : base(
            Environments.Development,
            new Dictionary<string, string?>
            {
                [AuthOptions.Pbkdf2IterationsVariable] = TestPbkdf2Iterations.ToString(),
            })
    {
    }
}
